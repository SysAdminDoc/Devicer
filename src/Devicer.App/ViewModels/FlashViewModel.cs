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

public partial class FlashViewModel : ObservableObject
{
    private readonly IOdinInspectorService _inspector;
    private DeviceInfo? _device;

    public ObservableCollection<TarEntryRow> Entries { get; } = new();

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

    public bool IsKnoxIntact => string.Equals(KnoxBit, "0", StringComparison.Ordinal);
    public bool IsKnoxTripped => !string.IsNullOrWhiteSpace(KnoxBit) && !IsKnoxIntact;

    public FlashViewModel(IOdinInspectorService inspector)
    {
        _inspector = inspector;
        // DryRunCommand's CanExecute reads Entries.Count, which doesn't fire any property
        // notification of its own. Without this hook the button stayed greyed out after
        // Inspect populated the list until WPF happened to poll CanExecute (e.g. on focus
        // change), giving the impression that Inspect failed silently.
        Entries.CollectionChanged += (_, _) => DryRunCommand.NotifyCanExecuteChanged();
    }

    public void PrefillFrom(DeviceInfo? device)
    {
        _device = device;
        KnoxBit = device?.KnoxWarrantyBit;
        OnPropertyChanged(nameof(IsKnoxIntact));
        OnPropertyChanged(nameof(IsKnoxTripped));
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
            lines.Add("⚠  EFS-CLEAR is ENABLED. This wipes /efs (IMEI / NVRAM). DO NOT proceed unless you have an EFS backup.");
        else
            lines.Add("EFS-Clear is OFF (correct default).");
        if (IsKnoxTripped)
            lines.Add($"⚠  Knox eFuse is TRIPPED on this device (warranty bit = {KnoxBit}). Custom-AP flashing has already been performed; further flashes won't restore Knox.");
        else if (IsKnoxIntact)
            lines.Add("Knox is intact (warranty bit = 0). Flashing a custom AP/kernel will trip it permanently.");
        lines.Add("");
        lines.Add($"Archive: {Info?.FileName} ({Info?.PackageHint ?? "?"})");
        lines.Add("Plan:");
        foreach (var e in selected)
            lines.Add($"  → {e.PartitionGuess,-16} ← {e.Name} ({e.SizeDisplay})");

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
}
