using System.IO;
using System.Text.RegularExpressions;

namespace Devicer.Core.Services;

public sealed record HeimdallFlashResult(
    bool Success,
    int TotalPartitions,
    int SucceededPartitions,
    IReadOnlyList<string> Warnings,
    string RawOutput
);

public interface IHeimdallService
{
    bool IsAvailable { get; }
    string? ToolPath { get; }

    Task<HeimdallFlashResult> FlashAsync(
        IReadOnlyList<FastbootFlashEntry> entries,
        bool noReboot,
        IProgress<FastbootFlashProgress>? progress,
        CancellationToken ct = default);

    Task<string?> DetectDeviceAsync(CancellationToken ct = default);
    Task<string> PrintPitAsync(CancellationToken ct = default);
}

public sealed class HeimdallService : IHeimdallService
{
    private readonly IShellRunner _shell;
    private readonly IToolManager _tools;
    private static readonly TimeSpan FlashTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FastTimeout = TimeSpan.FromSeconds(10);

    public HeimdallService(IShellRunner shell, IToolManager tools)
    {
        _shell = shell;
        _tools = tools;
    }

    public bool IsAvailable => _tools.Locate("heimdall").IsAvailable;
    public string? ToolPath => _tools.Locate("heimdall").Path;

    public async Task<string?> DetectDeviceAsync(CancellationToken ct = default)
    {
        var tool = _tools.Locate("heimdall");
        if (!tool.IsAvailable || tool.Path is null) return null;

        var r = await _shell.RunAsync(tool.Path, new[] { "detect" }, FastTimeout, ct).ConfigureAwait(false);
        return r.Success ? r.Stdout.Trim() : null;
    }

    public async Task<string> PrintPitAsync(CancellationToken ct = default)
    {
        var tool = _tools.Locate("heimdall");
        if (!tool.IsAvailable || tool.Path is null)
            return "Heimdall not found.";

        var r = await _shell.RunAsync(tool.Path, new[] { "print-pit" }, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        return r.Success ? r.Stdout : $"print-pit failed: {r.Stderr}";
    }

    public async Task<HeimdallFlashResult> FlashAsync(
        IReadOnlyList<FastbootFlashEntry> entries,
        bool noReboot,
        IProgress<FastbootFlashProgress>? progress,
        CancellationToken ct = default)
    {
        var tool = _tools.Locate("heimdall");
        if (!tool.IsAvailable || tool.Path is null)
            return new HeimdallFlashResult(false, 0, 0, ["Heimdall not found. Install it or set the path in Settings."], "");

        DevicerLog.Section($"Heimdall flash: {entries.Count} partitions");

        var args = new List<string> { "flash" };
        foreach (var e in entries)
        {
            args.Add("--" + e.Partition.ToUpperInvariant());
            args.Add(e.ImagePath);
        }
        if (noReboot)
            args.Add("--no-reboot");

        progress?.Report(new FastbootFlashProgress(
            FastbootFlashPhase.Flashing, 0, entries.Count, null,
            $"Flashing {entries.Count} partition(s) via Heimdall…"));

        var r = await _shell.RunAsync(tool.Path, args.ToArray(), FlashTimeout, ct).ConfigureAwait(false);
        var output = r.Stdout + "\n" + r.Stderr;

        var warnings = new List<string>();
        int succeeded = 0;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (Regex.IsMatch(trimmed, @"Uploading|Flashing", RegexOptions.IgnoreCase))
            {
                var partMatch = Regex.Match(trimmed, @"(?:Uploading|Flashing)\s+(\S+)", RegexOptions.IgnoreCase);
                var partName = partMatch.Success ? partMatch.Groups[1].Value : null;
                progress?.Report(new FastbootFlashProgress(
                    FastbootFlashPhase.Flashing, succeeded, entries.Count, partName,
                    $"{trimmed}"));
            }
            if (trimmed.Contains("successful", StringComparison.OrdinalIgnoreCase))
                succeeded++;
            if (trimmed.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
                warnings.Add(trimmed);
        }

        var success = r.Success && warnings.Count == 0;

        progress?.Report(new FastbootFlashProgress(
            success ? FastbootFlashPhase.Done : FastbootFlashPhase.Failed,
            entries.Count, entries.Count, null,
            success ? "Heimdall flash complete." : $"Heimdall finished with {warnings.Count} warning(s)."));

        return new HeimdallFlashResult(success, entries.Count, succeeded, warnings, output);
    }
}
