using System.IO;

namespace Devicer.Core.Services;

public enum FastbootFlashPhase
{
    Preparing,
    Flashing,
    SettingSlot,
    Rebooting,
    Done,
    Cancelled,
    Failed,
}

public sealed record FastbootFlashProgress(
    FastbootFlashPhase Phase,
    int PartitionIndex,
    int PartitionCount,
    string? PartitionName = null,
    string? Message = null
);

public sealed record FastbootFlashEntry(string Partition, string ImagePath);

public sealed record FastbootFlashResult(
    int TotalPartitions,
    int SucceededPartitions,
    IReadOnlyList<string> FailedPartitions,
    IReadOnlyList<string> WarningMessages
);

public interface IFastbootFlashService
{
    Task<FastbootFlashResult> FlashAsync(
        string serial,
        IReadOnlyList<FastbootFlashEntry> entries,
        string? setActiveSlot,
        bool rebootAfter,
        IProgress<FastbootFlashProgress>? progress,
        CancellationToken ct = default);

    Task<string> GeneratePlanAsync(
        string serial,
        IReadOnlyList<FastbootFlashEntry> entries,
        string? setActiveSlot,
        bool rebootAfter,
        CancellationToken ct = default);
}

public sealed class FastbootFlashService : IFastbootFlashService
{
    private readonly IFastbootService _fb;

    public FastbootFlashService(IFastbootService fb) => _fb = fb;

    public async Task<FastbootFlashResult> FlashAsync(
        string serial,
        IReadOnlyList<FastbootFlashEntry> entries,
        string? setActiveSlot,
        bool rebootAfter,
        IProgress<FastbootFlashProgress>? progress,
        CancellationToken ct = default)
    {
        DevicerLog.Section($"Fastboot flash: {entries.Count} partitions on {serial}");
        var failed = new List<string>();
        var warnings = new List<string>();
        int succeeded = 0;

        for (int i = 0; i < entries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var e = entries[i];
            progress?.Report(new FastbootFlashProgress(
                FastbootFlashPhase.Flashing, i, entries.Count, e.Partition,
                $"Flashing {e.Partition} ({i + 1}/{entries.Count})…"));

            if (!File.Exists(e.ImagePath))
            {
                warnings.Add($"{e.Partition}: file not found ({e.ImagePath})");
                failed.Add(e.Partition);
                continue;
            }

            var ok = await _fb.FlashAsync(serial, e.Partition, e.ImagePath, ct).ConfigureAwait(false);
            if (ok)
                succeeded++;
            else
            {
                failed.Add(e.Partition);
                warnings.Add($"{e.Partition}: fastboot flash returned non-zero exit code");
            }
        }

        if (!string.IsNullOrWhiteSpace(setActiveSlot))
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new FastbootFlashProgress(
                FastbootFlashPhase.SettingSlot, entries.Count, entries.Count, null,
                $"Setting active slot to {setActiveSlot}…"));

            var ok = await _fb.SetActiveSlotAsync(serial, setActiveSlot, ct).ConfigureAwait(false);
            if (!ok)
                warnings.Add($"set_active {setActiveSlot}: failed");
        }

        if (rebootAfter && failed.Count == 0)
        {
            progress?.Report(new FastbootFlashProgress(
                FastbootFlashPhase.Rebooting, entries.Count, entries.Count, null, "Rebooting…"));
            await _fb.RebootAsync(serial, ct).ConfigureAwait(false);
        }

        progress?.Report(new FastbootFlashProgress(
            failed.Count == 0 ? FastbootFlashPhase.Done : FastbootFlashPhase.Failed,
            entries.Count, entries.Count, null,
            $"Flashed {succeeded}/{entries.Count} partitions."));

        return new FastbootFlashResult(entries.Count, succeeded, failed, warnings);
    }

    public async Task<string> GeneratePlanAsync(
        string serial,
        IReadOnlyList<FastbootFlashEntry> entries,
        string? setActiveSlot,
        bool rebootAfter,
        CancellationToken ct = default)
    {
        var lines = new List<string>
        {
            "DRY RUN: no data will be written. The following plan would execute:",
            "",
            $"Target: {serial}",
        };

        var currentSlot = await _fb.GetVarAsync(serial, "current-slot", ct).ConfigureAwait(false);
        var isAB = currentSlot is not null;
        if (isAB)
            lines.Add($"Current slot: {currentSlot} (A/B device)");
        else
            lines.Add("Slot: single (non-A/B)");

        lines.Add("");
        lines.Add("Flash plan:");
        foreach (var e in entries)
        {
            var exists = File.Exists(e.ImagePath);
            var size = exists ? new FileInfo(e.ImagePath).Length : 0;
            var sizeStr = exists ? FormatBytes(size) : "FILE NOT FOUND";
            lines.Add($"  fastboot flash {e.Partition,-20} ← {Path.GetFileName(e.ImagePath)} ({sizeStr})");
        }

        if (!string.IsNullOrWhiteSpace(setActiveSlot))
        {
            lines.Add("");
            lines.Add($"  fastboot --set-active={setActiveSlot}");
        }

        if (rebootAfter)
        {
            lines.Add("");
            lines.Add("  fastboot reboot");
        }

        return string.Join('\n', lines);
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
