using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Devicer.Core.Models;
using Devicer.Core.Services;
using Microsoft.Win32;

namespace Devicer.App.ViewModels;

public partial class FastbootImageRow : ObservableObject
{
    [ObservableProperty]
    public partial bool Selected { get; set; } = true;

    [ObservableProperty]
    public partial string Partition { get; set; } = "";

    public required string FilePath { get; init; }
    public string FileName => Path.GetFileName(FilePath);
    public string SizeDisplay
    {
        get
        {
            if (!File.Exists(FilePath)) return "?";
            var bytes = new FileInfo(FilePath).Length;
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
}

public partial class FastbootFlashViewModel : ObservableObject
{
    private readonly IFastbootFlashService _fbFlash;
    private readonly IFastbootService _fb;
    private CancellationTokenSource? _flashCts;

    public ObservableCollection<FastbootImageRow> FastbootImages { get; } = new();
    public ObservableCollection<string> FlashWarnings { get; } = new();

    [ObservableProperty]
    public partial string? Diagnostic { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(FastbootDryRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(FastbootFlashCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelFlashCommand))]
    public partial bool IsFlashing { get; set; }

    [ObservableProperty]
    public partial string? FlashStatusText { get; set; }

    [ObservableProperty]
    public partial double? FlashProgressFraction { get; set; }

    [ObservableProperty]
    public partial string? FastbootSerial { get; set; }

    [ObservableProperty]
    public partial bool SetActiveSlot { get; set; }

    [ObservableProperty]
    public partial string ActiveSlotValue { get; set; } = "a";

    [ObservableProperty]
    public partial bool RebootAfterFlash { get; set; }

    [ObservableProperty]
    public partial bool DisableAvb { get; set; }

    public bool IsIdle => !IsFlashing;

    public FastbootFlashViewModel(IFastbootFlashService fbFlash, IFastbootService fb)
    {
        _fbFlash = fbFlash;
        _fb = fb;
        FastbootImages.CollectionChanged += (_, _) =>
        {
            FastbootDryRunCommand.NotifyCanExecuteChanged();
            FastbootFlashCommand.NotifyCanExecuteChanged();
        };
    }

    public void PrefillFrom(DeviceInfo? device)
    {
        FastbootSerial = device?.Serial;
    }

    [RelayCommand]
    public void AddFastbootImages()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select image file(s) to flash via fastboot",
            Filter = "Image files (*.img;*.bin)|*.img;*.bin|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var path in dlg.FileNames)
        {
            if (FastbootImages.Any(r => string.Equals(r.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            FastbootImages.Add(new FastbootImageRow
            {
                FilePath = path,
                Partition = GuessPartition(Path.GetFileName(path)),
            });
        }
    }

    [RelayCommand]
    public void RemoveFastbootImage(FastbootImageRow? row)
    {
        if (row is not null) FastbootImages.Remove(row);
    }

    [RelayCommand]
    public void ClearFastbootImages() => FastbootImages.Clear();

    [RelayCommand(CanExecute = nameof(CanFastbootDryRun))]
    public async Task FastbootDryRunAsync()
    {
        var entries = BuildFastbootEntries();
        if (entries.Count == 0)
        {
            Diagnostic = "Select at least one image.";
            return;
        }
        if (string.IsNullOrWhiteSpace(FastbootSerial))
        {
            Diagnostic = "No device serial. Connect a device in fastboot mode.";
            return;
        }
        var plan = await _fbFlash.GeneratePlanAsync(
            FastbootSerial!,
            entries,
            SetActiveSlot ? ActiveSlotValue : null,
            RebootAfterFlash).ConfigureAwait(true);
        Diagnostic = null;
        FlashStatusText = plan;
    }

    private bool CanFastbootDryRun() => FastbootImages.Count > 0 && !IsFlashing;

    [RelayCommand(CanExecute = nameof(CanFastbootFlash))]
    public async Task FastbootFlashAsync()
    {
        var entries = BuildFastbootEntries();
        if (entries.Count == 0)
        {
            Diagnostic = "Select at least one image.";
            return;
        }
        if (string.IsNullOrWhiteSpace(FastbootSerial))
        {
            Diagnostic = "No device serial. Connect a device in fastboot mode.";
            return;
        }

        IsFlashing = true;
        Diagnostic = null;
        FlashWarnings.Clear();
        FlashStatusText = "Starting flash…";
        FlashProgressFraction = null;

        _flashCts?.Dispose();
        _flashCts = new CancellationTokenSource();
        var ct = _flashCts.Token;

        var progress = new Progress<FastbootFlashProgress>(p =>
        {
            FlashStatusText = p.Message ?? $"{p.Phase} {p.PartitionName}";
            FlashProgressFraction = p.PartitionCount > 0
                ? Math.Clamp(p.PartitionIndex / (double)p.PartitionCount, 0, 1)
                : null;
        });

        try
        {
            var result = await _fbFlash.FlashAsync(
                FastbootSerial!,
                entries,
                SetActiveSlot ? ActiveSlotValue : null,
                RebootAfterFlash,
                progress, ct).ConfigureAwait(true);

            foreach (var w in result.WarningMessages) FlashWarnings.Add(w);

            if (result.FailedPartitions.Count == 0)
                FlashStatusText = $"Flashed {result.SucceededPartitions}/{result.TotalPartitions} partitions successfully.";
            else
            {
                FlashStatusText = $"Flashed {result.SucceededPartitions}/{result.TotalPartitions}. Failed: {string.Join(", ", result.FailedPartitions)}.";
                Diagnostic = "Some partitions failed to flash. Check the warnings below.";
            }
        }
        catch (OperationCanceledException)
        {
            FlashStatusText = "Flash cancelled.";
            FlashProgressFraction = null;
        }
        catch (Exception ex)
        {
            Diagnostic = $"Flash failed: {ex.Message}";
            FlashStatusText = null;
            FlashProgressFraction = null;
        }
        finally
        {
            IsFlashing = false;
        }
    }

    private bool CanFastbootFlash() => FastbootImages.Count > 0 && !IsFlashing;

    [RelayCommand(CanExecute = nameof(CanCancelFlash))]
    public void CancelFlash() => _flashCts?.Cancel();

    private bool CanCancelFlash() => IsFlashing;

    private IReadOnlyList<FastbootFlashEntry> BuildFastbootEntries()
    {
        return FastbootImages
            .Where(r => r.Selected && !string.IsNullOrWhiteSpace(r.Partition))
            .Select(r => new FastbootFlashEntry(r.Partition, r.FilePath))
            .ToList();
    }

    internal static string GuessPartition(string fileName)
    {
        var name = fileName.ToLowerInvariant();
        foreach (var ext in new[] { ".img", ".bin", ".lz4" })
        {
            if (name.EndsWith(ext, StringComparison.Ordinal))
                name = name[..^ext.Length];
        }
        if (name.StartsWith("ap_", StringComparison.Ordinal) ||
            name.StartsWith("bl_", StringComparison.Ordinal) ||
            name.StartsWith("cp_", StringComparison.Ordinal))
            name = name[3..];
        return name;
    }
}
