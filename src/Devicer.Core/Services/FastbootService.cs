using Devicer.Core.Models;

namespace Devicer.Core.Services;

public sealed record FastbootDevice(string Serial);

public interface IFastbootService
{
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FastbootDevice>> ListDevicesAsync(CancellationToken ct = default);
    Task<string?> GetVarAsync(string serial, string name, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetAllVarsAsync(string serial, CancellationToken ct = default);
    Task<bool> FlashAsync(string serial, string partition, string imagePath, CancellationToken ct = default);
    Task<bool> EraseAsync(string serial, string partition, CancellationToken ct = default);
    Task<bool> SetActiveSlotAsync(string serial, string slot, CancellationToken ct = default);
    Task<bool> RebootAsync(string serial, CancellationToken ct = default);
    Task<bool> RebootBootloaderAsync(string serial, CancellationToken ct = default);
    Task<bool> FlashDisableAvbAsync(string serial, string vbmetaPath, CancellationToken ct = default);
}

public sealed class FastbootService : IFastbootService
{
    private const string Fastboot = "fastboot";
    private static readonly TimeSpan FastTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan SlowTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan FlashTimeout = TimeSpan.FromMinutes(10);

    private readonly IShellRunner _shell;

    public FastbootService(IShellRunner shell) => _shell = shell;

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await _shell.RunAsync(Fastboot, new[] { "--version" }, FastTimeout, ct).ConfigureAwait(false);
            return r.Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<FastbootDevice>> ListDevicesAsync(CancellationToken ct = default)
    {
        var r = await _shell.RunAsync(Fastboot, new[] { "devices" }, FastTimeout, ct).ConfigureAwait(false);
        var list = new List<FastbootDevice>();
        if (!r.Success) return list;
        foreach (var raw in r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            if (!string.Equals(parts[1].Trim(), "fastboot", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(new FastbootDevice(parts[0].Trim()));
        }
        return list;
    }

    public async Task<string?> GetVarAsync(string serial, string name, CancellationToken ct = default)
    {
        var r = await _shell.RunAsync(Fastboot, new[] { "-s", serial, "getvar", name }, FastTimeout, ct).ConfigureAwait(false);
        var combined = string.IsNullOrWhiteSpace(r.Stderr) ? r.Stdout : r.Stderr;
        foreach (var raw in combined.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (!line.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
            var sep = line.IndexOf(':');
            if (sep < 0) continue;
            return line[(sep + 1)..].Trim();
        }
        return null;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllVarsAsync(string serial, CancellationToken ct = default)
    {
        var r = await _shell.RunAsync(Fastboot, new[] { "-s", serial, "getvar", "all" }, SlowTimeout, ct).ConfigureAwait(false);
        var combined = string.IsNullOrWhiteSpace(r.Stderr) ? r.Stdout : r.Stderr;
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in combined.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            const string boot = "(bootloader)";
            if (line.StartsWith(boot, StringComparison.OrdinalIgnoreCase))
                line = line[boot.Length..].Trim();

            var sep = line.IndexOf(':');
            if (sep <= 0) continue;
            var k = line[..sep].Trim();
            var v = line[(sep + 1)..].Trim();
            if (k.Length == 0) continue;
            dict[k] = v;
        }
        return dict;
    }

    public async Task<bool> FlashAsync(string serial, string partition, string imagePath, CancellationToken ct = default)
    {
        DevicerLog.Info("Fastboot", $"flash {partition} ← {imagePath}");
        var r = await _shell.RunAsync(Fastboot, new[] { "-s", serial, "flash", partition, imagePath }, FlashTimeout, ct).ConfigureAwait(false);
        if (!r.Success)
            DevicerLog.Error("Fastboot", $"flash {partition} failed (exit {r.ExitCode}): {r.Stderr}");
        return r.Success;
    }

    public async Task<bool> EraseAsync(string serial, string partition, CancellationToken ct = default)
    {
        DevicerLog.Info("Fastboot", $"erase {partition}");
        var r = await _shell.RunAsync(Fastboot, new[] { "-s", serial, "erase", partition }, SlowTimeout, ct).ConfigureAwait(false);
        return r.Success;
    }

    public async Task<bool> SetActiveSlotAsync(string serial, string slot, CancellationToken ct = default)
    {
        DevicerLog.Info("Fastboot", $"set_active {slot}");
        var r = await _shell.RunAsync(Fastboot, new[] { "-s", serial, "--set-active=" + slot }, FastTimeout, ct).ConfigureAwait(false);
        return r.Success;
    }

    public async Task<bool> RebootAsync(string serial, CancellationToken ct = default)
    {
        var r = await _shell.RunAsync(Fastboot, new[] { "-s", serial, "reboot" }, FastTimeout, ct).ConfigureAwait(false);
        return r.Success;
    }

    public async Task<bool> RebootBootloaderAsync(string serial, CancellationToken ct = default)
    {
        var r = await _shell.RunAsync(Fastboot, new[] { "-s", serial, "reboot-bootloader" }, FastTimeout, ct).ConfigureAwait(false);
        return r.Success;
    }

    public async Task<bool> FlashDisableAvbAsync(string serial, string vbmetaPath, CancellationToken ct = default)
    {
        DevicerLog.Info("Fastboot", $"flash vbmeta with --disable-verity --disable-verification ← {vbmetaPath}");
        var r = await _shell.RunAsync(Fastboot,
            new[] { "-s", serial, "--disable-verity", "--disable-verification", "flash", "vbmeta", vbmetaPath },
            FlashTimeout, ct).ConfigureAwait(false);
        if (!r.Success)
            DevicerLog.Error("Fastboot", $"flash vbmeta failed (exit {r.ExitCode}): {r.Stderr}");
        return r.Success;
    }
}
