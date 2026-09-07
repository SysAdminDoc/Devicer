using System.IO;
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
    private readonly IHashService _hash;
    private readonly string _root;

    private static readonly JsonSerializerOptions ManifestJsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public BackupService(IAdbService adb, IHashService hash, string? rootDir = null)
    {
        _adb = adb;
        _hash = hash;
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

                // `set -e` makes dd's non-zero exit propagate out of the script. Without it,
                // the trailing `chmod` would mask a dd failure and we'd happily pull an empty
                // or short image. `2>&1` keeps dd's stats/errors in stdout so we can surface
                // them in the warning text.
                var ddCmd = $"set -e; rm -f {Bash.Quote(remoteTmp)}; dd if={Bash.Quote(p.BlockPath)} of={Bash.Quote(remoteTmp)} bs=4M 2>&1; chmod 0644 {Bash.Quote(remoteTmp)}";
                var ddTimeout = EstimateDumpTimeout(p.SizeBytes);
                var ddRes = await _adb.RunSuAsync(serial, ddCmd, ddTimeout, ct).ConfigureAwait(false);
                if (!ddRes.Success)
                {
                    warnings.Add($"{p.Name}: dd failed (exit {ddRes.ExitCode}): {Tail(JoinStreams(ddRes), 400)}");
                    await TryCleanup(serial, remoteTmp).ConfigureAwait(false);
                    continue;
                }

                progress?.Report(new BackupProgress(BackupPhase.Pulling, p.Name, i, partitions.Count, 0, p.SizeBytes,
                    $"Pulling {p.Name} to host…"));

                var pullRes = await _adb.PullAsync(serial, remoteTmp, localPath, EstimatePullTimeout(p.SizeBytes), ct).ConfigureAwait(false);
                if (!pullRes.Success || !File.Exists(localPath))
                {
                    warnings.Add($"{p.Name}: adb pull failed: {Tail(JoinStreams(pullRes), 400)}");
                    await TryCleanup(serial, remoteTmp).ConfigureAwait(false);
                    continue;
                }

                // Sanity: a successful dd should land an image whose size matches blockdev's
                // reported partition size. Wide divergence (>1%) usually means dd was killed
                // mid-stream by an OOM / SELinux / shell-timeout that didn't propagate.
                var actual = new FileInfo(localPath).Length;
                if (p.SizeBytes > 0 && actual > 0 && Math.Abs(actual - p.SizeBytes) > Math.Max(p.SizeBytes / 100, 4096))
                {
                    warnings.Add($"{p.Name}: backup size {actual:N0} differs from expected {p.SizeBytes:N0} bytes: image may be truncated.");
                }

                progress?.Report(new BackupProgress(BackupPhase.Hashing, p.Name, i, partitions.Count, 0, p.SizeBytes,
                    $"Hashing {p.Name}…"));
                var sha = await _hash.ComputeSha256Async(localPath, ct).ConfigureAwait(false);
                var size = new FileInfo(localPath).Length;

                entries.Add(new PartitionBackupEntry
                {
                    Name = p.Name,
                    FileName = imageName,
                    SizeBytes = size,
                    Sha256 = sha,
                    IsCritical = p.IsCritical,
                });

                await TryCleanup(serial, remoteTmp).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                progress?.Report(new BackupProgress(BackupPhase.Cancelled, p.Name, i, partitions.Count, 0, p.SizeBytes, "Cancelled."));
                // Cleanup must use a fresh token: the user-cancelled `ct` would immediately
                // abort the rm and leave a multi-GB tmpfile on /data/local/tmp.
                await TryCleanup(serial, remoteTmp).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                warnings.Add($"{p.Name}: {ex.Message}");
                await TryCleanup(serial, remoteTmp).ConfigureAwait(false);
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

    private async Task TryCleanup(string serial, string remoteTmp)
    {
        // Cleanup is best-effort and runs even after the caller's CancellationToken fires
        // (otherwise the user would see "Cancelled" while a multi-GB image lingers in
        // /data/local/tmp and fills the device's data partition on the next attempt).
        using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await _adb.RunSuAsync(serial, $"rm -f {Bash.Quote(remoteTmp)}", TimeSpan.FromSeconds(10), cleanupCts.Token).ConfigureAwait(false); }
        catch { /* best-effort */ }
    }

    private static string JoinStreams(ShellResult r)
    {
        var so = (r.Stdout ?? string.Empty).Trim();
        var se = (r.Stderr ?? string.Empty).Trim();
        if (so.Length == 0) return se;
        if (se.Length == 0) return so;
        return so + " | " + se;
    }

    private static string Tail(string s, int max) => s.Length <= max ? s : "…" + s[^max..];

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
