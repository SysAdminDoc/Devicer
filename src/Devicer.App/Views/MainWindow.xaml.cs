using System.Windows;
using Devicer.App.Services;
using Devicer.App.ViewModels;

namespace Devicer.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var deviceVm = new DeviceViewModel(App.Host.DeviceProbe);
        DataContext = new MainViewModel(deviceVm);

        // Kick off an initial probe so the Device page lands on real data.
        Loaded += async (_, _) => await deviceVm.RefreshAsync();
    }
}
