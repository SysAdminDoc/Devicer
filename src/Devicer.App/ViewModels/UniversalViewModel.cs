using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private OemKind _detectedOem;

    [ObservableProperty]
    private string? _detectedOemDisplay;

    [ObservableProperty]
    private string? _model;

    [ObservableProperty]
    private string? _codename;

    [ObservableProperty]
    private OemGuidance? _guide;

    [ObservableProperty]
    private string? _diagnostic;

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
        try { Process.Start(new ProcessStartInfo { FileName = step.Url, UseShellExecute = true }); }
        catch (Exception ex) { Diagnostic = $"Could not open URL: {ex.Message}"; }
    }

    [RelayCommand]
    public void OpenPortal()
    {
        var url = Guide?.PortalUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch (Exception ex) { Diagnostic = $"Could not open URL: {ex.Message}"; }
    }
}
