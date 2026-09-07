using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Devicer.Core.Models;

namespace Devicer.Core.Services;

public enum RestorePhase
{
    Validating,
    Hashing,
    Pushing,
    Writing,
    Done,
    Cancelled,
    Failed,
}

public sealed record RestoreProgress(
    RestorePhase Phase,
    int PartitionIndex,
    int PartitionCount,
    string? PartitionName = null,
    string? Message = null
);

public sealed record RestoreResult(
    int TotalPartitions,
    int SucceededPartitions,
    IReadOnlyList<string> FailedPartitions,
    IReadOnlyList<string> WarningMessages
);

public interface IRestoreService
{
    Task<BackupManifest?> LoadManifestAsync(string folderPath, CancellationToken ct = default);

    Task<RestoreResult> RestoreAsync(
        string serial,
        string manifestFolderPath,
        IReadOnlyList<PartitionBackupEntry> selectedPartitions,
        IProgress<RestoreProgress>? progress,
        CancellationToken ct = default);

    Task<string> GeneratePlanAsync(
        string manifestFolderPath,
        IReadOnlyList<PartitionBackupEntry> selectedPartitions,
        CancellationToken ct = default);
}

public sealed class RestoreService : IRestoreService
{
    private readonly IAdbService _adb;
    private readonly IHashService _hash;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public RestoreService(IAdbService adb, IHashService hash)
    {
        _adb = adb;
        _hash = hash;
    }

    public Task<BackupManifest?> LoadManifestAsync(string folderPath, CancellationToken ct = default)
    {
        var path = Path.Combine(folderPath, "manifest.json");
        if (!File.Exists(path)) return Task.FromResult<BackupManifest?>(null);
        try
        {
            var json = File.ReadAllText(path);
            return Task.FromResult(JsonSerializer.Deserialize<BackupManifest>(json, JsonOpts));
        }
        catch
        {
            return Task.FromResult<BackupManifest?>(null);
        }
    }

    public async Task<string> GeneratePlanAsync(
        string manifestFolderPath,
        IReadOnlyList<PartitionBackupEntry> selectedPartitions,
        CancellationToken ct = default)
    {
        var lines = new List<string>
        {
            "DRY RUN: no data will be written. The following restore plan would execute:",
            "",
        };

        foreach (var p in selectedPartitions)
        {
            var imgPath = Path.Combine(manifestFolderPath, p.FileName);
            var exists = File.Exists(imgPath);
            var sizeStr = exists ? FormatBytes(new FileInfo(imgPath).Length) : "FILE NOT FOUND";
            var critical = p.IsCritical ? " [CRITICAL]" : "";
            lines.Add($"  dd of=/dev/block/by-name/{p.Name} <- {p.FileName} ({sizeStr}){critical}");
            if (exists && !string.IsNullOrWhiteSpace(p.Sha256))
                lines.Add($"    SHA256 pre-verify: {p.Sha256}");
        }

        lines.Add("");
        lines.Add("This OVERWRITES device partitions. This CANNOT be undone without another backup.");

        return string.Join('\n', lines);
    }

    public async Task<RestoreResult> RestoreAsync(
        string serial,
        string manifestFolderPath,
        IReadOnlyList<PartitionBackupEntry> selectedPartitions,
        IProgress<RestoreProgress>? progress,
        CancellationToken ct = default)
    {
        DevicerLog.Section($"Restore: {selectedPartitions.Count} partitions on {serial}");

        var failed = new List<string>();
        var warnings = new List<string>();
        int succeeded = 0;

        for (int i = 0; i < selectedPartitions.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var p = selectedPartitions[i];
            var localPath = Path.Combine(manifestFolderPath, p.FileName);

            progress?.Report(new RestoreProgress(
                RestorePhase.Validating, i, selectedPartitions.Count, p.Name,
                $"Validating {p.Name}…"));

            if (!File.Exists(localPath))
            {
                warnings.Add($"{p.Name}: file not found ({p.FileName})");
                failed.Add(p.Name);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(p.Sha256))
            {
                progress?.Report(new RestoreProgress(
                    RestorePhase.Hashing, i, selectedPartitions.Count, p.Name,
                    $"Verifying SHA256 for {p.Name}…"));

                var actualHash = await _hash.ComputeSha256Async(localPath, ct).ConfigureAwait(false);
                if (!string.Equals(actualHash, p.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"{p.Name}: SHA256 mismatch (expected {p.Sha256}, got {actualHash}). Refusing to write corrupt data.");
                    failed.Add(p.Name);
                    continue;
                }
            }

            progress?.Report(new RestoreProgress(
                RestorePhase.Pushing, i, selectedPartitions.Count, p.Name,
                $"Pushing {p.FileName} to device…"));

            var remoteTmp = $"/data/local/tmp/devicer_restore_{p.Name}.img";
            var pushResult = await RunAdbPushAsync(serial, localPath, remoteTmp, ct).ConfigureAwait(false);
            if (!pushResult.Success)
            {
                warnings.Add($"{p.Name}: adb push failed: {pushResult.Stderr.Trim()}");
                failed.Add(p.Name);
                continue;
            }

            progress?.Report(new RestoreProgress(
                RestorePhase.Writing, i, selectedPartitions.Count, p.Name,
                $"Writing {p.Name} to /dev/block/by-name/{p.Name}…"));

            var ddCmd = $"dd if={Bash.Quote(remoteTmp)} of=/dev/block/by-name/{Bash.Quote(p.Name)} bs=4M 2>&1";
            var fileSize = new FileInfo(localPath).Length;
            var estimatedTimeout = TimeSpan.FromSeconds(Math.Max(60, fileSize / (10L * 1024 * 1024) * 2));
            if (estimatedTimeout > TimeSpan.FromMinutes(30)) estimatedTimeout = TimeSpan.FromMinutes(30);

            var ddResult = await _adb.RunSuAsync(serial, ddCmd, estimatedTimeout, ct).ConfigureAwait(false);
            if (!ddResult.Success)
            {
                warnings.Add($"{p.Name}: dd write failed (exit {ddResult.ExitCode}): {ddResult.Stderr.Trim()}");
                failed.Add(p.Name);
            }
            else
            {
                succeeded++;
                DevicerLog.Info("Restore", $"{p.Name}: dd completed: {ddResult.Stdout.Trim()}");
            }

            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await _adb.RunSuAsync(serial, $"rm -f {Bash.Quote(remoteTmp)}", TimeSpan.FromSeconds(10), cleanupCts.Token).ConfigureAwait(false);
            }
            catch { }
        }

        progress?.Report(new RestoreProgress(
            failed.Count == 0 ? RestorePhase.Done : RestorePhase.Failed,
            selectedPartitions.Count, selectedPartitions.Count, null,
            $"Restored {succeeded}/{selectedPartitions.Count} partitions."));

        return new RestoreResult(selectedPartitions.Count, succeeded, failed, warnings);
    }

    private async Task<ShellResult> RunAdbPushAsync(string serial, string localPath, string remotePath, CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("adb")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        psi.ArgumentList.Add("-s"); psi.ArgumentList.Add(serial);
        psi.ArgumentList.Add("push"); psi.ArgumentList.Add(localPath); psi.ArgumentList.Add(remotePath);

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start adb.");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(30));

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = proc.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        return new ShellResult(proc.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string FormatBytes(long bytes)
    {
        const long k = 1024, m = k * 1024, g = m * 1024;
        return bytes switch
        {
            >= g => $"{bytes / (double)g:0.00} GB",
            >= m => $"{bytes / (double)m:0.0} MB",
            >= k => $"{bytes / (double)k:0.0} KB",
            _ => $"{bytes} B",
        };
    }
}
