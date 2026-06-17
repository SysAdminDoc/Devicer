using System.IO;
using System.Text.RegularExpressions;

namespace Devicer.Core.Services;

public enum ThorFlashPhase
{
    Connecting,
    Flashing,
    Verifying,
    Done,
    Cancelled,
    Failed,
}

public sealed record ThorFlashProgress(
    ThorFlashPhase Phase,
    int PartitionIndex,
    int PartitionCount,
    string? PartitionName = null,
    string? Message = null,
    double? FractionComplete = null
);

public sealed record ThorFlashResult(
    bool Success,
    int TotalPartitions,
    int SucceededPartitions,
    IReadOnlyList<string> Warnings,
    string RawOutput
);

public interface IThorService
{
    bool IsAvailable { get; }
    string? ToolPath { get; }

    Task<ThorFlashResult> FlashArchiveAsync(
        string archivePath,
        IReadOnlyList<string>? selectedPartitions,
        bool efsClear,
        IProgress<ThorFlashProgress>? progress,
        CancellationToken ct = default);
}

public sealed class ThorService : IThorService
{
    private readonly IShellRunner _shell;
    private readonly IToolManager _tools;
    private static readonly TimeSpan FlashTimeout = TimeSpan.FromMinutes(30);

    public ThorService(IShellRunner shell, IToolManager tools)
    {
        _shell = shell;
        _tools = tools;
    }

    public bool IsAvailable => _tools.Locate("thor").IsAvailable;
    public string? ToolPath => _tools.Locate("thor").Path;

    public async Task<ThorFlashResult> FlashArchiveAsync(
        string archivePath,
        IReadOnlyList<string>? selectedPartitions,
        bool efsClear,
        IProgress<ThorFlashProgress>? progress,
        CancellationToken ct = default)
    {
        var tool = _tools.Locate("thor");
        if (!tool.IsAvailable || tool.Path is null)
            return new ThorFlashResult(false, 0, 0, ["Thor Flash Utility not found. Install it or set the path in Settings."], "");

        DevicerLog.Section($"Thor flash: {archivePath}");
        progress?.Report(new ThorFlashProgress(ThorFlashPhase.Connecting, 0, 0, null, "Connecting to device…"));

        var args = new List<string> { "flash" };
        if (efsClear)
            args.Add("--efsclear");
        args.Add(archivePath);

        var r = await _shell.RunAsync(tool.Path, args.ToArray(), FlashTimeout, ct).ConfigureAwait(false);
        var output = r.Stdout + "\n" + r.Stderr;

        var warnings = new List<string>();
        int totalParts = 0;
        int succeededParts = 0;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("Flashing", StringComparison.OrdinalIgnoreCase))
            {
                totalParts++;
                var partName = ExtractPartitionName(trimmed);
                progress?.Report(new ThorFlashProgress(
                    ThorFlashPhase.Flashing, totalParts - 1, totalParts, partName,
                    $"Flashing {partName ?? "partition"}…"));
            }
            if (trimmed.Contains("success", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("done", StringComparison.OrdinalIgnoreCase))
            {
                succeededParts++;
            }
            if (trimmed.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("fail", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(trimmed);
            }
        }

        if (succeededParts == 0 && totalParts == 0 && r.Success)
            succeededParts = 1;

        var success = r.Success && warnings.Count == 0;

        progress?.Report(new ThorFlashProgress(
            success ? ThorFlashPhase.Done : ThorFlashPhase.Failed,
            totalParts, totalParts, null,
            success ? "Flash complete." : $"Flash finished with {warnings.Count} warning(s)."));

        return new ThorFlashResult(success, totalParts, succeededParts, warnings, output);
    }

    private static string? ExtractPartitionName(string line)
    {
        var match = Regex.Match(line, @"Flashing\s+(\S+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }
}
