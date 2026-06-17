using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
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
        var backupVm = new BackupViewModel(App.Host.Adb, App.Host.Backup, App.Host.Restore);
        var patchVm = new PatchViewModel(App.Host.BootPatch, App.Host.PcPatch);
        var odinVm = new OdinFlashViewModel(App.Host.OdinInspector, App.Host.Thor);
        var fastbootVm = new FastbootFlashViewModel(App.Host.FastbootFlash, App.Host.Fastboot);
        var flashVm = new FlashPageViewModel(odinVm, fastbootVm);
        var debloatVm = new DebloatViewModel(App.Host.Debloat);
        var universalVm = new UniversalViewModel(App.Host.OemGuidance);
        var settingsVm = new SettingsViewModel(
            App.Host.SettingsStore,
            App.Host.Theme,
            App.Host.Adb,
            App.Host.Fastboot);

        settingsVm.ProbeIntervalChanged += (_, secs) => deviceVm.SetProbeInterval(secs);

        deviceVm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(deviceVm.SelectedDevice))
            {
                firmwareVm.PrefillFrom(deviceVm.SelectedDevice);
                romVm.PrefillFrom(deviceVm.SelectedDevice);
                backupVm.PrefillFrom(deviceVm.SelectedDevice);
                patchVm.PrefillFrom(deviceVm.SelectedDevice);
                flashVm.PrefillFrom(deviceVm.SelectedDevice);
                debloatVm.PrefillFrom(deviceVm.SelectedDevice);
                universalVm.PrefillFrom(deviceVm.SelectedDevice);
            }
        };

        DataContext = new MainViewModel(deviceVm, firmwareVm, romVm, backupVm, patchVm, flashVm, debloatVm, universalVm, settingsVm);

        App.Host.Snackbar.PropertyChanged += OnSnackbarChanged;

        Loaded += async (_, _) => await deviceVm.RefreshAsync();
    }

    private void OnSnackbarChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SnackbarService.IsVisible)) return;
        var svc = App.Host.Snackbar;
        if (svc.IsVisible)
        {
            SnackbarText.Text = svc.Message;
            SnackbarText.Foreground = (Brush)FindResource("AppForeground");
            SnackbarHost.Background = svc.Severity switch
            {
                SnackbarSeverity.Success => new SolidColorBrush(Color.FromArgb(0xDD, 0x40, 0xA0, 0x2B)),
                SnackbarSeverity.Warning => new SolidColorBrush(Color.FromArgb(0xDD, 0xDF, 0x8E, 0x1D)),
                SnackbarSeverity.Error => new SolidColorBrush(Color.FromArgb(0xDD, 0xD2, 0x00, 0x32)),
                _ => new SolidColorBrush(Color.FromArgb(0xDD, 0x45, 0x47, 0x5A)),
            };
            SnackbarHost.Visibility = Visibility.Visible;
        }
        else
        {
            SnackbarHost.Visibility = Visibility.Collapsed;
        }
    }
}
