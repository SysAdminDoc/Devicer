using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Devicer.Core.Models;

namespace Devicer.Core.Services;

public sealed record TetherbackResult(
    bool Success,
    string OutputFolder,
    IReadOnlyList<string> BackedUpPartitions,
    IReadOnlyList<string> Warnings,
    string RawOutput
);

public interface ITetherbackService
{
    bool IsAvailable { get; }

    Task<TetherbackResult> BackupAsync(
        string serial,
        IProgress<BackupProgress>? progress,
        CancellationToken ct = default);
}

public sealed class TetherbackService : ITetherbackService
{
    private readonly IShellRunner _shell;
    private readonly IToolManager _tools;
    private readonly string _outRoot;

    public TetherbackService(IShellRunner shell, IToolManager tools, string? outRoot = null)
    {
        _shell = shell;
        _tools = tools;
        _outRoot = outRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Devicer", "backups");
    }

    public bool IsAvailable => _tools.Locate("tetherback").IsAvailable;

    public async Task<TetherbackResult> BackupAsync(
        string serial,
        IProgress<BackupProgress>? progress,
        CancellationToken ct = default)
    {
        var tool = _tools.Locate("tetherback");
        if (!tool.IsAvailable || tool.Path is null)
            throw new InvalidOperationException("tetherback not found. Install it or set the path in Settings.");

        DevicerLog.Section($"tetherback backup: {serial}");

        var stamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd_HHmmss");
        var folder = Path.Combine(_outRoot, SafeSlug(serial), $"tetherback_{stamp}");
        Directory.CreateDirectory(folder);

        progress?.Report(new BackupProgress(BackupPhase.Preparing, "", 0, 0, 0, null, "Starting tetherback…"));

        var args = new List<string>
        {
            "--serial", serial,
            "--output-dir", folder,
        };

        var r = await _shell.RunAsync(tool.Path, args.ToArray(), TimeSpan.FromHours(2), ct).ConfigureAwait(false);
        var output = r.Stdout + "\n" + r.Stderr;

        var warnings = new List<string>();
        var partitions = new List<string>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("Backing up", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("dumping", StringComparison.OrdinalIgnoreCase))
            {
                partitions.Add(trimmed);
                progress?.Report(new BackupProgress(BackupPhase.DumpingOnDevice, trimmed, partitions.Count, 0, 0, null, trimmed));
            }
            if (trimmed.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
                warnings.Add(trimmed);
        }

        progress?.Report(new BackupProgress(
            r.Success ? BackupPhase.Done : BackupPhase.Failed,
            "", partitions.Count, partitions.Count, 0, null,
            r.Success ? $"tetherback complete. {partitions.Count} partitions." : "tetherback finished with errors."));

        return new TetherbackResult(r.Success, folder, partitions, warnings, output);
    }

    private static string SafeSlug(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = input.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
