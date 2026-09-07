using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Devicer.Core.Models;
using Devicer.Core.Services;
using Microsoft.Win32;

namespace Devicer.App.ViewModels;

public partial class TarEntryRow : ObservableObject
{
    [ObservableProperty]
    public partial bool Selected { get; set; } = true;

    public required OdinTarEntry Entry { get; init; }
}

public partial class OdinFlashViewModel : ObservableObject
{
    private readonly IOdinInspectorService _inspector;
    private readonly IThorService _thor;
    private DeviceInfo? _device;
    private CancellationTokenSource? _flashCts;

    public ObservableCollection<TarEntryRow> Entries { get; } = new();
    public ObservableCollection<string> FlashWarnings { get; } = new();

    [ObservableProperty]
    public partial string? ArchivePath { get; set; }

    [ObservableProperty]
    public partial OdinTarInfo? Info { get; set; }

    [ObservableProperty]
    public partial bool EfsClearEnabled { get; set; }

    [ObservableProperty]
    public partial string? KnoxBit { get; set; }

    [ObservableProperty]
    public partial string? Diagnostic { get; set; }

    [ObservableProperty]
    public partial string? StatusText { get; set; }

    [ObservableProperty]
    public partial bool IsInspecting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CancelFlashCommand))]
    public partial bool IsFlashing { get; set; }

    [ObservableProperty]
    public partial string? FlashStatusText { get; set; }

    [ObservableProperty]
    public partial double? FlashProgressFraction { get; set; }

    [ObservableProperty]
    public partial bool ThorConfirmed { get; set; }

    public bool IsKnoxIntact => string.Equals(KnoxBit, "0", StringComparison.Ordinal);
    public bool IsKnoxTripped => !string.IsNullOrWhiteSpace(KnoxBit) && !IsKnoxIntact;
    public bool IsIdle => !IsFlashing;
    public bool IsThorAvailable => _thor.IsAvailable;
    public bool IsOneUi8BootloaderLocked => _device?.IsSamsung == true
        && IsOneUi8OrNewer(_device.OneUiVersion)
        && _device.OemUnlockSupported != true;

    public OdinFlashViewModel(IOdinInspectorService inspector, IThorService thor)
    {
        _inspector = inspector;
        _thor = thor;
        Entries.CollectionChanged += (_, _) => DryRunCommand.NotifyCanExecuteChanged();
    }

    public void PrefillFrom(DeviceInfo? device)
    {
        _device = device;
        KnoxBit = device?.KnoxWarrantyBit;
        OnPropertyChanged(nameof(IsKnoxIntact));
        OnPropertyChanged(nameof(IsKnoxTripped));
        OnPropertyChanged(nameof(IsOneUi8BootloaderLocked));
    }

    private static bool IsOneUi8OrNewer(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return false;
        var dot = version.IndexOf('.');
        var major = dot > 0 ? version[..dot] : version;
        return int.TryParse(major, out var v) && v >= 8;
    }

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
            StatusText = $"{info.Entries.Count} entries: {info.PackageHint ?? "(unknown package)"} {(info.HasMd5Suffix ? "[.md5 trailer]" : string.Empty)}";
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
            "DRY RUN: no data was written. The following plan would execute:",
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

    [RelayCommand(CanExecute = nameof(CanThorFlash))]
    public async Task ThorFlashAsync()
    {
        if (string.IsNullOrWhiteSpace(ArchivePath))
        {
            Diagnostic = "Select an Odin archive first.";
            return;
        }
        if (!ThorConfirmed)
        {
            Diagnostic = "Check the confirmation box to proceed with Thor flash. This writes to the device and cannot be undone.";
            return;
        }

        IsFlashing = true;
        Diagnostic = null;
        FlashWarnings.Clear();
        FlashStatusText = "Starting Thor flash…";
        FlashProgressFraction = null;
        ThorConfirmed = false;

        _flashCts?.Dispose();
        _flashCts = new CancellationTokenSource();
        var ct = _flashCts.Token;

        var progress = new Progress<ThorFlashProgress>(p =>
        {
            FlashStatusText = p.Message ?? $"{p.Phase} {p.PartitionName}";
            FlashProgressFraction = p.FractionComplete;
        });

        try
        {
            var selected = Entries.Where(r => r.Selected).Select(r => r.Entry.PartitionGuess).ToList();
            var result = await _thor.FlashArchiveAsync(
                ArchivePath!,
                selected.Count > 0 ? selected : null,
                EfsClearEnabled,
                progress, ct).ConfigureAwait(true);

            foreach (var w in result.Warnings) FlashWarnings.Add(w);

            FlashStatusText = result.Success
                ? "Thor flash complete."
                : $"Thor flash finished with warnings. {result.SucceededPartitions}/{result.TotalPartitions} partitions.";
        }
        catch (OperationCanceledException)
        {
            FlashStatusText = "Thor flash cancelled.";
            FlashProgressFraction = null;
        }
        catch (Exception ex)
        {
            Diagnostic = $"Thor flash failed: {ex.Message}";
            FlashStatusText = null;
            FlashProgressFraction = null;
        }
        finally
        {
            IsFlashing = false;
        }
    }

    private bool CanThorFlash() => !IsFlashing && !string.IsNullOrWhiteSpace(ArchivePath) && Entries.Count > 0;

    [RelayCommand(CanExecute = nameof(CanCancelFlash))]
    public void CancelFlash() => _flashCts?.Cancel();

    private bool CanCancelFlash() => IsFlashing;
}
