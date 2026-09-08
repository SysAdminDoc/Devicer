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
    public partial bool IsProbing { get; set; }

    [ObservableProperty]
    public partial string? Diagnostic { get; set; }

    [ObservableProperty]
    public partial DeviceInfo? SelectedDevice { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Idle";

    public ObservableCollection<DeviceInfo> Devices { get; } = new();

    public DeviceViewModel(IDeviceProbeService probe, int probeIntervalSeconds = 4, bool enablePolling = true)
    {
        _probe = probe;

        // Hot-plug poller: re-probe at the user-configured interval if not already probing.
        // Cheap (`adb devices` is fast) and a sensible upper bound on UX latency for "user just plugged in".
        _hotplugTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(probeIntervalSeconds, 2, 30)) };
        _hotplugTimer.Tick += async (_, _) => await PollAsync();
        if (enablePolling)
            _hotplugTimer.Start();
    }

    public void SetProbeInterval(int seconds)
    {
        _hotplugTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 2, 30));
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
        // Throttle: never re-probe within 80% of the configured interval, regardless of timer drift.
        var minGap = _hotplugTimer.Interval.TotalSeconds * 0.8;
        if ((DateTime.UtcNow - _lastProbeUtc).TotalSeconds < minGap) return;
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
