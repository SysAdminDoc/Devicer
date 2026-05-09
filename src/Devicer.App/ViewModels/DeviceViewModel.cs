using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Devicer.Core.Models;
using Devicer.Core.Services;

namespace Devicer.App.ViewModels;

public partial class DeviceViewModel : ObservableObject
{
    private readonly IDeviceProbeService _probe;

    [ObservableProperty]
    private bool _isProbing;

    [ObservableProperty]
    private string? _diagnostic;

    [ObservableProperty]
    private DeviceInfo? _selectedDevice;

    public ObservableCollection<DeviceInfo> Devices { get; } = new();

    public DeviceViewModel(IDeviceProbeService probe)
    {
        _probe = probe;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsProbing) return;
        IsProbing = true;
        Diagnostic = null;
        try
        {
            var result = await _probe.ProbeAsync().ConfigureAwait(true);
            Devices.Clear();
            foreach (var d in result.Devices) Devices.Add(d);
            SelectedDevice = Devices.FirstOrDefault();
            if (Devices.Count == 0)
                Diagnostic = result.Diagnostic ?? "No devices detected. Plug in a phone with USB debugging enabled, or boot it into Download / Fastboot mode.";
        }
        catch (Exception ex)
        {
            Diagnostic = $"Probe failed: {ex.Message}";
        }
        finally
        {
            IsProbing = false;
        }
    }
}
