using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Devicer.Core.Models;
using Devicer.Core.Services;

namespace Devicer.App.ViewModels;

public partial class PackageRow : ObservableObject
{
    [ObservableProperty]
    public partial bool Selected { get; set; }

    public required InstalledPackage Package { get; init; }
}

public partial class DebloatViewModel : ObservableObject
{
    private readonly IDebloatService _debloat;
    private DeviceInfo? _device;

    public ObservableCollection<PackageRow> Packages { get; } = new();

    [ObservableProperty]
    public partial string? Serial { get; set; }

    [ObservableProperty]
    public partial string? Model { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(LoadPackagesCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableSelectedCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? StatusText { get; set; }

    [ObservableProperty]
    public partial string? Diagnostic { get; set; }

    [ObservableProperty]
    public partial string? FilterText { get; set; }

    public bool IsIdle => !IsBusy;

    public DebloatViewModel(IDebloatService debloat) => _debloat = debloat;

    public void PrefillFrom(DeviceInfo? device)
    {
        _device = device;
        Serial = device?.Serial;
        Model = device?.Model;
    }

    [RelayCommand(CanExecute = nameof(CanLoadPackages))]
    public async Task LoadPackagesAsync()
    {
        if (string.IsNullOrWhiteSpace(Serial)) return;
        IsBusy = true;
        Diagnostic = null;
        StatusText = "Loading installed packages…";
        Packages.Clear();
        try
        {
            var packages = await _debloat.ListPackagesAsync(Serial!).ConfigureAwait(true);
            foreach (var p in packages)
                Packages.Add(new PackageRow { Package = p });
            StatusText = $"{packages.Count} packages ({packages.Count(p => p.IsEnabled)} enabled, {packages.Count(p => !p.IsEnabled)} disabled)";
        }
        catch (Exception ex)
        {
            Diagnostic = $"Could not list packages: {ex.Message}";
            StatusText = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLoadPackages() => !IsBusy && !string.IsNullOrWhiteSpace(Serial);

    [RelayCommand(CanExecute = nameof(CanBatchOp))]
    public async Task DisableSelectedAsync()
    {
        if (string.IsNullOrWhiteSpace(Serial)) return;
        var selected = Packages.Where(r => r.Selected && r.Package.IsEnabled).ToList();
        if (selected.Count == 0) { Diagnostic = "Select at least one enabled package."; return; }

        IsBusy = true;
        Diagnostic = null;
        int ok = 0;
        foreach (var r in selected)
        {
            StatusText = $"Disabling {r.Package.PackageName}…";
            if (await _debloat.DisablePackageAsync(Serial!, r.Package.PackageName).ConfigureAwait(true))
                ok++;
        }
        StatusText = $"Disabled {ok}/{selected.Count} packages. Reload to refresh state.";
        IsBusy = false;
    }

    [RelayCommand(CanExecute = nameof(CanBatchOp))]
    public async Task EnableSelectedAsync()
    {
        if (string.IsNullOrWhiteSpace(Serial)) return;
        var selected = Packages.Where(r => r.Selected && !r.Package.IsEnabled).ToList();
        if (selected.Count == 0) { Diagnostic = "Select at least one disabled package."; return; }

        IsBusy = true;
        Diagnostic = null;
        int ok = 0;
        foreach (var r in selected)
        {
            StatusText = $"Enabling {r.Package.PackageName}…";
            if (await _debloat.EnablePackageAsync(Serial!, r.Package.PackageName).ConfigureAwait(true))
                ok++;
        }
        StatusText = $"Enabled {ok}/{selected.Count} packages. Reload to refresh state.";
        IsBusy = false;
    }

    private bool CanBatchOp() => !IsBusy && !string.IsNullOrWhiteSpace(Serial);
}
