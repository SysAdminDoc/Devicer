using System.Windows;
using Devicer.App.Services;
using Devicer.App.ViewModels;

namespace Devicer.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var deviceVm = new DeviceViewModel(App.Host.DeviceProbe, App.Host.SettingsStore.Settings.ProbeIntervalSeconds);
        var firmwareVm = new FirmwareViewModel(App.Host.FirmwareCheck);
        var settingsVm = new SettingsViewModel(
            App.Host.SettingsStore,
            App.Host.Theme,
            App.Host.Adb,
            App.Host.Fastboot);

        // When the user changes the probe interval in Settings, push it into the live DeviceViewModel.
        settingsVm.ProbeIntervalChanged += (_, secs) => deviceVm.SetProbeInterval(secs);

        // Auto-fill Firmware tab from whatever device the user has selected on the Device tab.
        deviceVm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(deviceVm.SelectedDevice))
                firmwareVm.PrefillFrom(deviceVm.SelectedDevice);
        };

        DataContext = new MainViewModel(deviceVm, firmwareVm, settingsVm);

        // Kick off an initial probe so the Device page lands on real data.
        Loaded += async (_, _) => await deviceVm.RefreshAsync();
    }
}
