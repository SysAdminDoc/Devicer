using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Devicer.Core.Models;
using Devicer.Core.Services;

namespace Devicer.App.ViewModels;

public partial class DeviceViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceProbeService _probe;
    private readonly DispatcherTimer _hotplugTimer;
    private DateTime _lastProbeUtc = DateTime.MinValue;

    [ObservableProperty]
    private bool _isProbing;

    [ObservableProperty]
    private string? _diagnostic;

    [ObservableProperty]
    private DeviceInfo? _selectedDevice;

    [ObservableProperty]
    private string _statusText = "Idle";

    public ObservableCollection<DeviceInfo> Devices { get; } = new();

    public DeviceViewModel(IDeviceProbeService probe)
    {
        _probe = probe;

        // Hot-plug poller: re-probe every 4s if not already probing. Cheap (adb devices is fast)
        // and a sensible upper bound on UX latency for "user just plugged in a phone".
        _hotplugTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _hotplugTimer.Tick += async (_, _) => await PollAsync();
        _hotplugTimer.Start();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsProbing) return;
        IsProbing = true;
        StatusText = "Probing adb / fastboot…";
        Diagnostic = null;
        try
        {
            var result = await _probe.ProbeAsync().ConfigureAwait(true);
            ApplyProbeResult(result);
            _lastProbeUtc = DateTime.UtcNow;
            StatusText = Devices.Count switch
            {
                0 => "No devices",
                1 => "1 device connected",
                var n => $"{n} devices connected",
            };
        }
        catch (Exception ex)
        {
            Diagnostic = $"Probe failed: {ex.Message}";
            StatusText = "Probe failed";
        }
        finally
        {
            IsProbing = false;
        }
    }

    private async Task PollAsync()
    {
        if (IsProbing) return;
        // Throttle: don't re-probe more than once every 3.5s, even if the timer ticks early.
        if ((DateTime.UtcNow - _lastProbeUtc).TotalSeconds < 3.5) return;
        await RefreshAsync();
    }

    private void ApplyProbeResult(ProbeResult result)
    {
        // Preserve selection across refreshes when the same serial is still present.
        var prevSerial = SelectedDevice?.Serial;

        Devices.Clear();
        foreach (var d in result.Devices) Devices.Add(d);

        SelectedDevice = (prevSerial is not null
            ? Devices.FirstOrDefault(d => d.Serial == prevSerial)
            : null) ?? Devices.FirstOrDefault();

        if (Devices.Count == 0)
            Diagnostic = result.Diagnostic ?? "No devices detected. Plug in a phone with USB debugging enabled, or boot it into Download / Fastboot mode.";
        else
            Diagnostic = null;
    }

    public void Dispose()
    {
        _hotplugTimer.Stop();
    }
}
