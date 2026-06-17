using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Devicer.Core.Models;
using Devicer.Core.Services;
using Microsoft.Win32;

namespace Devicer.App.ViewModels;

public partial class TarEntryRow : ObservableObject
{
    [ObservableProperty]
    private bool _selected = true;

    public required OdinTarEntry Entry { get; init; }
}

public partial class FastbootImageRow : ObservableObject
{
    [ObservableProperty]
    private bool _selected = true;

    [ObservableProperty]
    private string _partition;

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

    public FastbootImageRow() => _partition = "";
}

public partial class FlashViewModel : ObservableObject
{
    private readonly IOdinInspectorService _inspector;
    private readonly IFastbootFlashService _fbFlash;
    private readonly IFastbootService _fb;
    private DeviceInfo? _device;
    private CancellationTokenSource? _flashCts;

    public ObservableCollection<TarEntryRow> Entries { get; } = new();
    public ObservableCollection<FastbootImageRow> FastbootImages { get; } = new();
    public ObservableCollection<string> FlashWarnings { get; } = new();

    [ObservableProperty]
    private string? _archivePath;

    [ObservableProperty]
    private OdinTarInfo? _info;

    [ObservableProperty]
    private bool _efsClearEnabled;

    [ObservableProperty]
    private string? _knoxBit;

    [ObservableProperty]
    private string? _diagnostic;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private bool _isInspecting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(FastbootDryRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(FastbootFlashCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelFlashCommand))]
    private bool _isFlashing;

    [ObservableProperty]
    private string? _flashStatusText;

    [ObservableProperty]
    private double? _flashProgressFraction;

    [ObservableProperty]
    private string? _fastbootSerial;

    [ObservableProperty]
    private bool _setActiveSlot;

    [ObservableProperty]
    private string _activeSlotValue = "a";

    [ObservableProperty]
    private bool _rebootAfterFlash;

    public bool IsKnoxIntact => string.Equals(KnoxBit, "0", StringComparison.Ordinal);
    public bool IsKnoxTripped => !string.IsNullOrWhiteSpace(KnoxBit) && !IsKnoxIntact;
    public bool IsIdle => !IsFlashing;
    public bool IsSamsung => _device?.Manufacturer?.Contains("samsung", StringComparison.OrdinalIgnoreCase) == true;

    public FlashViewModel(IOdinInspectorService inspector, IFastbootFlashService fbFlash, IFastbootService fb)
    {
        _inspector = inspector;
        _fbFlash = fbFlash;
        _fb = fb;
        Entries.CollectionChanged += (_, _) => DryRunCommand.NotifyCanExecuteChanged();
        FastbootImages.CollectionChanged += (_, _) =>
        {
            FastbootDryRunCommand.NotifyCanExecuteChanged();
            FastbootFlashCommand.NotifyCanExecuteChanged();
        };
    }

    public void PrefillFrom(DeviceInfo? device)
    {
        _device = device;
        KnoxBit = device?.KnoxWarrantyBit;
        FastbootSerial = device?.Serial;
        OnPropertyChanged(nameof(IsKnoxIntact));
        OnPropertyChanged(nameof(IsKnoxTripped));
        OnPropertyChanged(nameof(IsSamsung));
    }

    // ── Samsung Odin section ──

    [RelayCommand]
    public void BrowseArchive()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Odin firmware archive (AP / CP / CSC / BL .tar.md5 or .tar)",
            Filter = "Odin tarballs (*.tar.md5;*.tar)|*.tar.md5;*.tar|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() == true) ArchivePath = dlg.FileName;
    }

    [RelayCommand(CanExecute = nameof(CanInspect))]
    public async Task InspectAsync()
    {
        if (string.IsNullOrWhiteSpace(ArchivePath)) return;
        IsInspecting = true;
        Diagnostic = null;
        StatusText = "Reading archive…";
        Entries.Clear();
        try
        {
            var info = await _inspector.InspectAsync(ArchivePath!).ConfigureAwait(true);
            Info = info;
            foreach (var e in info.Entries)
                Entries.Add(new TarEntryRow { Entry = e, Selected = e.IsImage });
            StatusText = $"{info.Entries.Count} entries — {info.PackageHint ?? "(unknown package)"} {(info.HasMd5Suffix ? "[.md5 trailer]" : string.Empty)}";
        }
        catch (Exception ex)
        {
            Diagnostic = $"Could not read archive: {ex.Message}";
            StatusText = null;
        }
        finally
        {
            IsInspecting = false;
        }
    }

    private bool CanInspect() => !IsInspecting && !string.IsNullOrWhiteSpace(ArchivePath);

    [RelayCommand(CanExecute = nameof(CanDryRun))]
    public void DryRun()
    {
        var selected = Entries.Where(r => r.Selected).Select(r => r.Entry).ToList();
        if (selected.Count == 0)
        {
            Diagnostic = "Select at least one image to dry-run.";
            return;
        }

        var lines = new List<string>
        {
            "DRY RUN — no data was written. The following plan would execute:",
            "",
        };
        if (EfsClearEnabled)
            lines.Add("EFS-CLEAR is ENABLED. This wipes /efs (IMEI / NVRAM). DO NOT proceed unless you have an EFS backup.");
        else
            lines.Add("EFS-Clear is OFF (correct default).");
        if (IsKnoxTripped)
            lines.Add($"Knox eFuse is TRIPPED on this device (warranty bit = {KnoxBit}). Custom-AP flashing has already been performed; further flashes won't restore Knox.");
        else if (IsKnoxIntact)
            lines.Add("Knox is intact (warranty bit = 0). Flashing a custom AP/kernel will trip it permanently.");
        lines.Add("");
        lines.Add($"Archive: {Info?.FileName} ({Info?.PackageHint ?? "?"})");
        lines.Add("Plan:");
        foreach (var e in selected)
            lines.Add($"  -> {e.PartitionGuess,-16} <- {e.Name} ({e.SizeDisplay})");

        Diagnostic = null;
        StatusText = string.Join('\n', lines);
    }

    private bool CanDryRun() => Entries.Count > 0;

    partial void OnArchivePathChanged(string? value)
    {
        InspectCommand.NotifyCanExecuteChanged();
        Entries.Clear();
        Info = null;
    }

    partial void OnIsInspectingChanged(bool value) => InspectCommand.NotifyCanExecuteChanged();

    // ── Fastboot flash section ──

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

    private static string GuessPartition(string fileName)
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
