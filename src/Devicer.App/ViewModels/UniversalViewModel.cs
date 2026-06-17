using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Devicer.App.Services;
using Devicer.Core.Models;
using Devicer.Core.Services;

namespace Devicer.App.ViewModels;

public partial class UniversalViewModel : ObservableObject
{
    private readonly IOemGuidanceService _guidance;

    public ObservableCollection<OemStep> UnlockSteps { get; } = new();
    public ObservableCollection<OemStep> FlashSteps { get; } = new();
    public ObservableCollection<OemStep> Quirks { get; } = new();

    [ObservableProperty]
    public partial OemKind DetectedOem { get; set; }

    [ObservableProperty]
    public partial string? DetectedOemDisplay { get; set; }

    [ObservableProperty]
    public partial string? Model { get; set; }

    [ObservableProperty]
    public partial string? Codename { get; set; }

    [ObservableProperty]
    public partial OemGuidance? Guide { get; set; }

    [ObservableProperty]
    public partial string? Diagnostic { get; set; }

    public UniversalViewModel(IOemGuidanceService guidance)
    {
        _guidance = guidance;
        Apply(OemKind.Unknown);
    }

    public void PrefillFrom(DeviceInfo? device)
    {
        if (device is null)
        {
            Apply(OemKind.Unknown);
            Model = null; Codename = null;
            return;
        }
        Model = device.Model;
        Codename = device.Codename;
        var oem = OemKindExtensions.Detect(device.Manufacturer, device.Brand);
        Apply(oem);
    }

    private void Apply(OemKind oem)
    {
        DetectedOem = oem;
        DetectedOemDisplay = oem.DisplayName();
        Guide = _guidance.For(oem);

        UnlockSteps.Clear();
        foreach (var s in Guide.UnlockSteps) UnlockSteps.Add(s);
        FlashSteps.Clear();
        foreach (var s in Guide.FlashSteps) FlashSteps.Add(s);
        Quirks.Clear();
        foreach (var s in Guide.Quirks) Quirks.Add(s);
    }

    [RelayCommand]
    public void OpenStep(OemStep? step)
    {
        if (step is null || string.IsNullOrWhiteSpace(step.Url)) return;
        var err = UrlLauncher.TryOpen(step.Url);
        if (err is not null) Diagnostic = err;
    }

    [RelayCommand]
    public void OpenPortal()
    {
        var err = UrlLauncher.TryOpen(Guide?.PortalUrl);
        if (err is not null && Guide?.PortalUrl is not null) Diagnostic = err;
    }
}
