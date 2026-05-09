using Devicer.Core.Models;

namespace Devicer.Core.Services;

public sealed record AdbDevice(string Serial, ConnectionState State);

public interface IAdbService
{
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetAllPropsAsync(string serial, CancellationToken ct = default);
    Task<string?> GetPropAsync(string serial, string key, CancellationToken ct = default);
    Task<RootStatus> DetectRootAsync(string serial, CancellationToken ct = default);

    /// <summary>
    /// Reads the device IMEI via the privileged <c>iphonesubinfo</c> service. Android 10+
    /// gates this behind <c>READ_PRIVILEGED_PHONE_STATE</c>, so we route through <c>su</c>
    /// when root is present. Returns null if neither path yields a valid 14–15 digit IMEI.
    /// </summary>
    Task<string?> ReadImeiAsync(string serial, CancellationToken ct = default);
}

public sealed class AdbService : IAdbService
{
    private const string Adb = "adb";
    private static readonly TimeSpan FastTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan SlowTimeout = TimeSpan.FromSeconds(20);

    private readonly IShellRunner _shell;

    public AdbService(IShellRunner shell) => _shell = shell;

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await _shell.RunAsync(Adb, new[] { "version" }, FastTimeout, ct).ConfigureAwait(false);
            return r.Success && r.Stdout.Contains("Android Debug Bridge", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken ct = default)
    {
        var r = await _shell.RunAsync(Adb, new[] { "devices" }, FastTimeout, ct).ConfigureAwait(false);
        if (!r.Success) return Array.Empty<AdbDevice>();

        var list = new List<AdbDevice>();
        foreach (var raw in r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("*", StringComparison.Ordinal)) continue; // daemon notices

            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            list.Add(new AdbDevice(parts[0].Trim(), MapState(parts[1].Trim())));
        }
        return list;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllPropsAsync(string serial, CancellationToken ct = default)
    {
        var r = await _shell.RunAsync(Adb, new[] { "-s", serial, "shell", "getprop" }, SlowTimeout, ct).ConfigureAwait(false);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!r.Success) return dict;

        // getprop output: [key]: [value]
        foreach (var raw in r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length < 7) continue;
            // expected: [k]: [v]
            var keyStart = line.IndexOf('[');
            var keyEnd = line.IndexOf(']', keyStart + 1);
            if (keyStart < 0 || keyEnd <= keyStart) continue;
            var sep = line.IndexOf(':', keyEnd);
            if (sep < 0) continue;
            var valStart = line.IndexOf('[', sep);
            var valEnd = line.LastIndexOf(']');
            if (valStart < 0 || valEnd <= valStart) continue;

            var k = line.Substring(keyStart + 1, keyEnd - keyStart - 1);
            var v = line.Substring(valStart + 1, valEnd - valStart - 1);
            dict[k] = v;
        }
        return dict;
    }

    public async Task<string?> GetPropAsync(string serial, string key, CancellationToken ct = default)
    {
        var r = await _shell.RunAsync(Adb, new[] { "-s", serial, "shell", "getprop", key }, FastTimeout, ct).ConfigureAwait(false);
        if (!r.Success) return null;
        var v = r.Stdout.Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    public async Task<RootStatus> DetectRootAsync(string serial, CancellationToken ct = default)
    {
        // Probe Magisk via su -c. `su -c '<cmd>'` is the canonical Magisk invocation.
        var magisk = await TryProbe(serial, "magisk -c", ct).ConfigureAwait(false);
        if (magisk is not null)
            return new RootStatus(RootKind.Magisk, magisk);

        // KernelSU exposes `ksud` once installed.
        var ksud = await TryProbe(serial, "ksud --version", ct).ConfigureAwait(false);
        if (ksud is not null)
            return new RootStatus(RootKind.KernelSU, ksud);

        // APatch exposes `apd` similar to ksud.
        var apatch = await TryProbe(serial, "apd --version", ct).ConfigureAwait(false);
        if (apatch is not null)
            return new RootStatus(RootKind.APatch, apatch);

        // Generic su present without recognized manager = "Other".
        var generic = await TryProbe(serial, "id", ct).ConfigureAwait(false);
        if (generic is not null && generic.Contains("uid=0", StringComparison.Ordinal))
            return new RootStatus(RootKind.Other, "su present");

        return RootStatus.None;
    }

    public async Task<string?> ReadImeiAsync(string serial, CancellationToken ct = default)
    {
        // The classic invocation: `service call iphonesubinfo 1 i32 0` returns IMEI for SIM slot 0.
        // Output is a Parcel dump; we extract the 14–15 digit IMEI by stripping non-digits and
        // trimming the leading length byte. On modern Android this requires root.
        async Task<string?> Try(string cmd)
        {
            try
            {
                var r = await _shell.RunAsync(Adb, new[] { "-s", serial, "shell", cmd }, SlowTimeout, ct).ConfigureAwait(false);
                if (!r.Success) return null;
                return ExtractImeiFromServiceCall(r.Stdout);
            }
            catch
            {
                return null;
            }
        }

        // Direct (works only when shell has READ_PRIVILEGED_PHONE_STATE — almost never).
        var direct = await Try("service call iphonesubinfo 1 i32 0").ConfigureAwait(false);
        if (direct is not null) return direct;

        // Root path. Quoted: shell parser must keep the inner quotes.
        var rooted = await Try("su -c \"service call iphonesubinfo 1 i32 0\"").ConfigureAwait(false);
        if (rooted is not null) return rooted;

        // Some ROMs expose getDeviceId() at index 6.
        var alt = await Try("su -c \"service call iphonesubinfo 6 i32 0\"").ConfigureAwait(false);
        return alt;
    }

    internal static string? ExtractImeiFromServiceCall(string parcelDump)
    {
        // Parcel reply format example:
        //   Result: Parcel(
        //     0x00000000: 00000000 0000000f 00350033 00370030 '........3.5.0.7.'
        //     0x00000010: 00390032 00390039 00370030 00390035 '2.9.9.9.0.7.5.9.'
        //     0x00000020: 00000033                            '3.......        ')
        // We want the ASCII inside the single quotes, stripping dots/spaces/quotes/.
        if (string.IsNullOrWhiteSpace(parcelDump)) return null;
        var sb = new System.Text.StringBuilder();
        foreach (var line in parcelDump.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var firstQuote = line.IndexOf('\'');
            var lastQuote  = line.LastIndexOf('\'');
            if (firstQuote < 0 || lastQuote <= firstQuote) continue;
            var inner = line.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
            foreach (var c in inner)
                if (c >= '0' && c <= '9') sb.Append(c);
        }
        var digits = sb.ToString();
        // IMEI is 14 (without check digit) or 15 (with). Trim leading zeros that come from
        // the length-prefix half-words in the parcel, then take the trailing 15.
        if (digits.Length < 14) return null;
        if (digits.Length > 15) digits = digits[^15..];
        return digits;
    }

    private async Task<string?> TryProbe(string serial, string suCommand, CancellationToken ct)
    {
        try
        {
            var r = await _shell.RunAsync(
                Adb,
                new[] { "-s", serial, "shell", "su", "-c", suCommand },
                FastTimeout, ct).ConfigureAwait(false);
            if (!r.Success) return null;
            var v = r.Stdout.Trim();
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
        catch
        {
            return null;
        }
    }

    private static ConnectionState MapState(string state) => state switch
    {
        "device" => ConnectionState.Adb,
        "recovery" => ConnectionState.Recovery,
        "sideload" => ConnectionState.Sideload,
        "bootloader" or "fastboot" => ConnectionState.Fastboot,
        "unauthorized" => ConnectionState.Unauthorized,
        "offline" => ConnectionState.NotConnected,
        _ => ConnectionState.Unknown,
    };
}
