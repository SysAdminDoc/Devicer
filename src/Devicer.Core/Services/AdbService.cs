using Devicer.Core.Models;

namespace Devicer.Core.Services;

public sealed record AdbDevice(string Serial, ConnectionState State);

public interface IAdbService
{
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the platform-tools version string (e.g. "36.0.2") or null if adb is not found.
    /// ADB &lt; 36.0.2 has a Samsung device-detection bug and a Windows file-truncation bug
    /// during push/pull that corrupts transferred boot images.
    /// </summary>
    Task<string?> GetVersionAsync(CancellationToken ct = default);

    /// <summary>Minimum safe platform-tools version. Older versions have known Samsung bugs.</summary>
    static readonly Version MinSafeVersion = new(36, 0, 2);
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

    /// <summary>
    /// Last-resort fallback: triggers <c>*#06#</c> on the phone so the IMEI dialog
    /// pops on the device's screen. Used when programmatic reads are blocked
    /// (modern One UI 7+ / Android 14+ even denies privileged reads via root).
    /// </summary>
    Task<bool> ShowImeiOnPhoneAsync(string serial, CancellationToken ct = default);

    /// <summary>
    /// Lists the device's partitions via <c>/dev/block/by-name</c>. Requires root (the directory
    /// is unreadable as shell on most modern Android). Resolves each symlink to its block-device
    /// target and stats the file size.
    /// </summary>
    Task<IReadOnlyList<Models.PartitionInfo>> ListPartitionsAsync(string serial, CancellationToken ct = default);

    /// <summary>Run an arbitrary shell command (no <c>su</c>) and return raw stdout/stderr.</summary>
    Task<ShellResult> RunShellAsync(string serial, string command, TimeSpan? timeout = null, CancellationToken ct = default);

    /// <summary>Run a shell command via <c>su -c</c>. Returns the result; check <c>Success</c>.</summary>
    Task<ShellResult> RunSuAsync(string serial, string command, TimeSpan? timeout = null, CancellationToken ct = default);

    /// <summary>Pull a file off the device via <c>adb pull</c>.</summary>
    Task<ShellResult> PullAsync(string serial, string remotePath, string localPath, TimeSpan? timeout = null, CancellationToken ct = default);
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

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await _shell.RunAsync(Adb, new[] { "version" }, FastTimeout, ct).ConfigureAwait(false);
            if (!r.Success) return null;
            foreach (var line in r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("Version", StringComparison.OrdinalIgnoreCase)) continue;
                var dashIdx = trimmed.LastIndexOf('-');
                if (dashIdx < 0) continue;
                var ver = trimmed[(dashIdx + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(ver)) return ver;
            }
            return null;
        }
        catch { return null; }
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

    public Task<ShellResult> RunShellAsync(string serial, string command, TimeSpan? timeout = null, CancellationToken ct = default)
        => _shell.RunAsync(Adb, new[] { "-s", serial, "shell", command }, timeout ?? SlowTimeout, ct);

    public Task<ShellResult> RunSuAsync(string serial, string command, TimeSpan? timeout = null, CancellationToken ct = default)
        => _shell.RunAsync(Adb, new[] { "-s", serial, "shell", "su", "-c", command }, timeout ?? SlowTimeout, ct);

    public Task<ShellResult> PullAsync(string serial, string remotePath, string localPath, TimeSpan? timeout = null, CancellationToken ct = default)
        => _shell.RunAsync(Adb, new[] { "-s", serial, "pull", remotePath, localPath }, timeout ?? TimeSpan.FromMinutes(20), ct);

    public async Task<IReadOnlyList<Models.PartitionInfo>> ListPartitionsAsync(string serial, CancellationToken ct = default)
    {
        // ls -l prints: lrwxrwxrwx 1 root root 21 2026-05-08 16:39 efs -> /dev/block/sdc4
        // We need both the by-name slug (left) and the block-device target (right) so we can dd it.
        var ls = await RunSuAsync(serial, "ls -l /dev/block/by-name 2>/dev/null", TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
        if (!ls.Success || string.IsNullOrWhiteSpace(ls.Stdout)) return Array.Empty<Models.PartitionInfo>();

        var entries = new List<(string Name, string BlockPath)>();
        foreach (var raw in ls.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.TrimEnd('\r', ' ', '\t');
            // Look for "<name> -> <target>".
            var arrow = line.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow < 0) continue;
            var lhs = line[..arrow].TrimEnd();
            var rhs = line[(arrow + 4)..].Trim();
            // The slug is the last token on the LHS.
            var lastSpace = lhs.LastIndexOf(' ');
            var slug = lastSpace >= 0 ? lhs[(lastSpace + 1)..] : lhs;
            if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(rhs)) continue;
            entries.Add((slug, rhs));
        }

        if (entries.Count == 0) return Array.Empty<Models.PartitionInfo>();

        // Bulk-stat sizes via blockdev --getsize64. Batched into chunks because a single
        // shell command per device with 125+ quoted paths can blow past adb shell's
        // ~16 KB argv limit on some platform-tools builds (manifests as a silent failure
        // returning empty stdout). 40 paths/batch keeps every command well under 4 KB.
        const int BatchSize = 40;
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
        for (int batchStart = 0; batchStart < entries.Count; batchStart += BatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = entries.Skip(batchStart).Take(BatchSize);
            var statCmd = "for p in " + string.Join(' ', batch.Select(e => Bash.Quote(e.BlockPath))) + "; do echo -n \"$p|\"; blockdev --getsize64 \"$p\" 2>/dev/null || echo 0; done";
            var stat = await RunSuAsync(serial, statCmd, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
            if (!stat.Success) continue;

            foreach (var raw in stat.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.TrimEnd('\r');
                var pipe = line.IndexOf('|');
                if (pipe <= 0) continue;
                var path = line[..pipe];
                var sizeStr = line[(pipe + 1)..].Trim();
                if (long.TryParse(sizeStr, out var sz)) sizes[path] = sz;
            }
        }

        return entries.Select(e => new Models.PartitionInfo
        {
            Name = e.Name,
            BlockPath = e.BlockPath,
            SizeBytes = sizes.TryGetValue(e.BlockPath, out var s) ? s : 0,
            IsCritical = Models.PartitionInfo.CriticalNames.Contains(e.Name),
            CriticalReason = Models.PartitionInfo.ReasonFor(e.Name),
        }).OrderByDescending(p => p.IsCritical).ThenBy(p => p.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Opens the device's "About phone" / "Status" screen so the user can read the IMEI.
    /// Used as a fallback when modern Android (One UI 7+ / Android 14+) blocks all
    /// programmatic IMEI reads via the privileged service-binder. Returns true if the
    /// intent was dispatched.
    /// </summary>
    /// <remarks>
    /// We prefer the <c>android.settings.DEVICE_INFO_SETTINGS</c> action over the <c>*#06#</c>
    /// MMI dial: modern Samsung dialers reject MMI codes from <c>am start</c> with "Connection
    /// problem or invalid MMI code" because the calling package isn't a privileged dialer.
    /// The Settings deep-link reliably opens the About-phone screen on every Android version.
    /// </remarks>
    public async Task<bool> ShowImeiOnPhoneAsync(string serial, CancellationToken ct = default)
    {
        try
        {
            var r = await _shell.RunAsync(Adb,
                new[] { "-s", serial, "shell", "am", "start", "-a", "android.settings.DEVICE_INFO_SETTINGS" },
                FastTimeout, ct).ConfigureAwait(false);
            return r.Success;
        }
        catch
        {
            return false;
        }
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
            var lastQuote = line.LastIndexOf('\'');
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
        // Final sanity: reject obvious garbage that occasionally falls out of weird parcel
        // shapes — Samsung's late-2024 protocol change made `service call` return parcels
        // that include length-prefix runs of zeros, and we'd happily extract them as a
        // "valid" IMEI of 0000…. The Smart Switch backend rejects those with FUS 408 anyway,
        // but failing fast here surfaces the real cause instead of a confusing auth error.
        if (digits.All(c => c == '0')) return null;
        if (digits.Distinct().Count() == 1) return null;
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

    private static ConnectionState MapState(string state)
    {
        // adb sometimes returns multi-word states like "no permissions; user in plugdev?"
        // when the udev rules / WinUSB driver isn't right — keep the prefix so we still
        // classify them as Unauthorized rather than silently falling into Unknown.
        var first = state;
        var space = state.IndexOfAny(new[] { ' ', '\t', ';' });
        if (space > 0) first = state[..space];
        return first switch
        {
            "device" => ConnectionState.Adb,
            "recovery" => ConnectionState.Recovery,
            "sideload" => ConnectionState.Sideload,
            "bootloader" or "fastboot" => ConnectionState.Fastboot,
            "unauthorized" or "no" => ConnectionState.Unauthorized,
            "offline" => ConnectionState.NotConnected,
            "host" => ConnectionState.NotConnected, // emulator host entries; not a real device
            _ => ConnectionState.Unknown,
        };
    }
}
