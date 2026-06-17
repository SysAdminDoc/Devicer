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
        var firmwareVm = new FirmwareViewModel(App.Host.FirmwareCheck, App.Host.FirmwareDownloadFactory, App.Host.Adb, App.Host.ImeiCache);
        var romVm = new RomViewModel(App.Host.RomAggregator, App.Host.RomDownload);
        var backupVm = new BackupViewModel(App.Host.Adb, App.Host.Backup);
        var patchVm = new PatchViewModel(App.Host.BootPatch);
        var flashVm = new FlashViewModel(App.Host.OdinInspector, App.Host.FastbootFlash, App.Host.Fastboot, App.Host.Thor);
        var universalVm = new UniversalViewModel(App.Host.OemGuidance);
        var settingsVm = new SettingsViewModel(
            App.Host.SettingsStore,
            App.Host.Theme,
            App.Host.Adb,
            App.Host.Fastboot);

        // When the user changes the probe interval in Settings, push it into the live DeviceViewModel.
        settingsVm.ProbeIntervalChanged += (_, secs) => deviceVm.SetProbeInterval(secs);

        // Auto-fill Firmware + ROM tabs from whatever device the user has selected on the Device tab.
        deviceVm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(deviceVm.SelectedDevice))
            {
                firmwareVm.PrefillFrom(deviceVm.SelectedDevice);
                romVm.PrefillFrom(deviceVm.SelectedDevice);
                backupVm.PrefillFrom(deviceVm.SelectedDevice);
                patchVm.PrefillFrom(deviceVm.SelectedDevice);
                flashVm.PrefillFrom(deviceVm.SelectedDevice);
                universalVm.PrefillFrom(deviceVm.SelectedDevice);
            }
        };

        DataContext = new MainViewModel(deviceVm, firmwareVm, romVm, backupVm, patchVm, flashVm, universalVm, settingsVm);

        // Kick off an initial probe so the Device page lands on real data.
        Loaded += async (_, _) => await deviceVm.RefreshAsync();
    }
}
