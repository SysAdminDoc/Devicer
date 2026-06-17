using System.IO;

namespace Devicer.Core.Services;

public sealed record NeoBackupResult(
    bool Success,
    string? PulledArchivePath,
    IReadOnlyList<string> Warnings,
    string RawOutput
);

public interface INeoBackupService
{
    Task<bool> IsInstalledAsync(string serial, CancellationToken ct = default);

    Task<NeoBackupResult> TriggerBackupAsync(
        string serial,
        string? pullToFolder,
        IProgress<BackupProgress>? progress,
        CancellationToken ct = default);
}

public sealed class NeoBackupService : INeoBackupService
{
    private readonly IAdbService _adb;
    private readonly string _outRoot;

    private const string NeoPackage = "com.machiav3lli.backup";
    private const string NeoActivity = "com.machiav3lli.backup/.activities.MainActivityX";
    private const string NeoBackupDir = "/storage/emulated/0/NeoBackup";

    public NeoBackupService(IAdbService adb, string? outRoot = null)
    {
        _adb = adb;
        _outRoot = outRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Devicer", "backups");
    }

    public async Task<bool> IsInstalledAsync(string serial, CancellationToken ct = default)
    {
        var r = await _adb.RunShellAsync(serial, $"pm list packages {NeoPackage}", TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        return r.Success && r.Stdout.Contains(NeoPackage, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<NeoBackupResult> TriggerBackupAsync(
        string serial,
        string? pullToFolder,
        IProgress<BackupProgress>? progress,
        CancellationToken ct = default)
    {
        DevicerLog.Section($"Neo Backup trigger: {serial}");
        var warnings = new List<string>();

        progress?.Report(new BackupProgress(BackupPhase.Preparing, "", 0, 0, 0, null, "Checking Neo Backup installation…"));

        if (!await IsInstalledAsync(serial, ct).ConfigureAwait(false))
            return new NeoBackupResult(false, null, [$"Neo Backup ({NeoPackage}) is not installed on this device."], "");

        progress?.Report(new BackupProgress(BackupPhase.Preparing, "", 0, 0, 0, null, "Launching Neo Backup…"));

        var launchResult = await _adb.RunShellAsync(serial,
            $"am start -n {NeoActivity} -a android.intent.action.MAIN",
            TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);

        if (!launchResult.Success)
            warnings.Add($"Could not launch Neo Backup activity: {launchResult.Stderr.Trim()}");

        progress?.Report(new BackupProgress(BackupPhase.Preparing, "", 0, 0, 0, null,
            "Neo Backup launched. Complete the backup on the device, then the archive will be pulled."));

        await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);

        var lsResult = await _adb.RunShellAsync(serial,
            $"ls -1t {NeoBackupDir}/ 2>/dev/null | head -5",
            TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);

        var output = launchResult.Stdout + "\n" + lsResult.Stdout;
        string? pulledPath = null;

        if (!string.IsNullOrWhiteSpace(pullToFolder) && lsResult.Success)
        {
            var stamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd_HHmmss");
            var localFolder = Path.Combine(pullToFolder ?? _outRoot, SafeSlug(serial), $"neobackup_{stamp}");
            Directory.CreateDirectory(localFolder);

            progress?.Report(new BackupProgress(BackupPhase.Pulling, "", 0, 0, 0, null,
                $"Pulling Neo Backup archive from {NeoBackupDir}…"));

            var pullResult = await _adb.PullAsync(serial, NeoBackupDir, localFolder, TimeSpan.FromMinutes(30), ct).ConfigureAwait(false);
            if (pullResult.Success)
            {
                pulledPath = localFolder;
                DevicerLog.Info("NeoBackup", $"Pulled to {localFolder}");
            }
            else
            {
                warnings.Add($"adb pull from {NeoBackupDir} failed: {pullResult.Stderr.Trim()}");
            }
        }

        progress?.Report(new BackupProgress(
            warnings.Count == 0 ? BackupPhase.Done : BackupPhase.Failed,
            "", 0, 0, 0, null,
            pulledPath is not null ? $"Neo Backup pulled to {pulledPath}" : "Neo Backup launched — pull manually when backup completes."));

        return new NeoBackupResult(warnings.Count == 0, pulledPath, warnings, output);
    }

    private static string SafeSlug(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = input.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
