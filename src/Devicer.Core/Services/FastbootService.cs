using Devicer.Core.Models;

namespace Devicer.Core.Services;

public sealed record FastbootDevice(string Serial);

public interface IFastbootService
{
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FastbootDevice>> ListDevicesAsync(CancellationToken ct = default);
    Task<string?> GetVarAsync(string serial, string name, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetAllVarsAsync(string serial, CancellationToken ct = default);
}

public sealed class FastbootService : IFastbootService
{
    private const string Fastboot = "fastboot";
    private static readonly TimeSpan FastTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan SlowTimeout = TimeSpan.FromSeconds(20);

    private readonly IShellRunner _shell;

    public FastbootService(IShellRunner shell) => _shell = shell;

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await _shell.RunAsync(Fastboot, new[] { "--version" }, FastTimeout, ct).ConfigureAwait(false);
            // fastboot writes its banner to stdout on success.
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
        // fastboot writes getvar output to stderr historically, stdout on newer builds.
        var combined = string.IsNullOrWhiteSpace(r.Stderr) ? r.Stdout : r.Stderr;
        foreach (var raw in combined.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            // expected line: "<name>: <value>"
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
            // typical: "(bootloader) name: value"
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
}
