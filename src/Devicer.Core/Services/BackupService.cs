using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Devicer.Core.Models;

namespace Devicer.Core.Services;

public enum BackupPhase
{
    Preparing,
    DumpingOnDevice,
    Pulling,
    Hashing,
    WritingManifest,
    Done,
    Cancelled,
    Failed,
}

public sealed record BackupProgress(
    BackupPhase Phase,
    string PartitionName,
    int PartitionIndex,
    int PartitionCount,
    long BytesProcessed,
    long? TotalBytes,
    string? Message = null);

public sealed record BackupRunResult(
    string FolderPath,
    BackupManifest Manifest,
    IReadOnlyList<string> WarningMessages);

public interface IBackupService
{
    /// <summary>
    /// Backs up the selected partitions of the connected device by dd'ing each block on-device
    /// to a tmp file, pulling it to the host, and SHA256-verifying. Writes a manifest at the
    /// end. Folder layout: <c>%LOCALAPPDATA%\Devicer\backups\&lt;serial&gt;\&lt;timestamp&gt;\</c>.
    /// </summary>
    Task<BackupRunResult> RunAsync(
        string serial,
        DeviceInfo? deviceInfo,
        IReadOnlyList<PartitionInfo> partitions,
        IProgress<BackupProgress>? progress,
        CancellationToken ct = default);
}

public sealed class BackupService : IBackupService
{
    private readonly IAdbService _adb;
    private readonly string _root;

    private static readonly JsonSerializerOptions ManifestJsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public BackupService(IAdbService adb, string? rootDir = null)
    {
        _adb = adb;
        _root = rootDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Devicer", "backups");
    }

    public string Root => _root;

    public async Task<BackupRunResult> RunAsync(
        string serial,
        DeviceInfo? deviceInfo,
        IReadOnlyList<PartitionInfo> partitions,
        IProgress<BackupProgress>? progress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serial)) throw new ArgumentException("serial required", nameof(serial));
        if (partitions.Count == 0) throw new ArgumentException("at least one partition required", nameof(partitions));

        var stamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd_HHmmss");
        var folder = Path.Combine(_root, SafeSlug(serial), stamp);
        Directory.CreateDirectory(folder);

        var entries = new List<PartitionBackupEntry>();
        var warnings = new List<string>();

        for (int i = 0; i < partitions.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var p = partitions[i];
            progress?.Report(new BackupProgress(BackupPhase.Preparing, p.Name, i, partitions.Count, 0, p.SizeBytes,
                $"Preparing {p.Name} ({p.SizeDisplay})…"));

            var remoteTmp = $"/data/local/tmp/devicer_{p.Name}.img";
            var imageName = SafeSlug(p.Name) + ".img";
            var localPath = Path.Combine(folder, imageName);

            try
            {
                progress?.Report(new BackupProgress(BackupPhase.DumpingOnDevice, p.Name, i, partitions.Count, 0, p.SizeBytes,
                    $"dd'ing {p.Name} on device…"));

                // bs=4M for throughput; use root su. Kill any earlier failed attempts first.
                var ddCmd = $"rm -f {Bash.Quote(remoteTmp)}; dd if={Bash.Quote(p.BlockPath)} of={Bash.Quote(remoteTmp)} bs=4M 2>&1; chmod 0644 {Bash.Quote(remoteTmp)}";
                // dd of large partitions can take minutes. Generous timeout proportional to size.
                var ddTimeout = EstimateDumpTimeout(p.SizeBytes);
                var ddRes = await _adb.RunSuAsync(serial, ddCmd, ddTimeout, ct).ConfigureAwait(false);
                if (!ddRes.Success)
                {
                    warnings.Add($"{p.Name}: dd failed (exit {ddRes.ExitCode}). stderr: {ddRes.Stderr.Trim()}");
                    await TryCleanup(serial, remoteTmp, ct).ConfigureAwait(false);
                    continue;
                }

                progress?.Report(new BackupProgress(BackupPhase.Pulling, p.Name, i, partitions.Count, 0, p.SizeBytes,
                    $"Pulling {p.Name} to host…"));

                var pullRes = await _adb.PullAsync(serial, remoteTmp, localPath, EstimatePullTimeout(p.SizeBytes), ct).ConfigureAwait(false);
                if (!pullRes.Success || !File.Exists(localPath))
                {
                    warnings.Add($"{p.Name}: adb pull failed: {pullRes.Stderr.Trim()}");
                    await TryCleanup(serial, remoteTmp, ct).ConfigureAwait(false);
                    continue;
                }

                progress?.Report(new BackupProgress(BackupPhase.Hashing, p.Name, i, partitions.Count, 0, p.SizeBytes,
                    $"Hashing {p.Name}…"));
                var sha = await ComputeSha256Async(localPath, ct).ConfigureAwait(false);
                var size = new FileInfo(localPath).Length;

                entries.Add(new PartitionBackupEntry
                {
                    Name = p.Name,
                    FileName = imageName,
                    SizeBytes = size,
                    Sha256 = sha,
                    IsCritical = p.IsCritical,
                });

                await TryCleanup(serial, remoteTmp, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                progress?.Report(new BackupProgress(BackupPhase.Cancelled, p.Name, i, partitions.Count, 0, p.SizeBytes, "Cancelled."));
                await TryCleanup(serial, remoteTmp, ct).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                warnings.Add($"{p.Name}: {ex.Message}");
                await TryCleanup(serial, remoteTmp, ct).ConfigureAwait(false);
            }
        }

        progress?.Report(new BackupProgress(BackupPhase.WritingManifest, "(manifest)", partitions.Count, partitions.Count, 0, null, "Writing manifest…"));

        var manifest = new BackupManifest
        {
            Serial = serial,
            Model = deviceInfo?.Model,
            Codename = deviceInfo?.Codename,
            CreatedUtc = DateTimeOffset.UtcNow,
            Partitions = entries,
        };
        var manifestPath = Path.Combine(folder, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, ManifestJsonOpts), ct).ConfigureAwait(false);

        progress?.Report(new BackupProgress(BackupPhase.Done, "(done)", partitions.Count, partitions.Count, manifest.TotalBytes, manifest.TotalBytes,
            $"Saved {entries.Count}/{partitions.Count} partition(s) to {folder}"));

        return new BackupRunResult(folder, manifest, warnings);
    }

    private async Task TryCleanup(string serial, string remoteTmp, CancellationToken ct)
    {
        try { await _adb.RunSuAsync(serial, $"rm -f {Bash.Quote(remoteTmp)}", TimeSpan.FromSeconds(10), ct).ConfigureAwait(false); }
        catch { /* best-effort */ }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        using var sha = SHA256.Create();
        var buf = new byte[1 << 16];
        int n;
        while ((n = await fs.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
            sha.TransformBlock(buf, 0, n, null, 0);
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    private static TimeSpan EstimateDumpTimeout(long bytes)
    {
        // Allow ~10 MB/s worst-case (modern eMMC easily does 100+ MB/s; legacy slow flashes can dip below 20).
        var secs = Math.Max(60, bytes / (10L * 1024 * 1024));
        return TimeSpan.FromSeconds(Math.Min(secs, 30 * 60));
    }

    private static TimeSpan EstimatePullTimeout(long bytes)
    {
        // adb USB transfer ~30 MB/s; min 60 s.
        var secs = Math.Max(60, bytes / (15L * 1024 * 1024));
        return TimeSpan.FromSeconds(Math.Min(secs, 30 * 60));
    }

    private static string SafeSlug(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = input.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
