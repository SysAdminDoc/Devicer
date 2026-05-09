using System.Collections.ObjectModel;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Devicer.App.Views;

namespace Devicer.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<NavItem> NavItems { get; }

    [ObservableProperty]
    private NavItem? _selectedNavItem;

    [ObservableProperty]
    private UserControl? _currentPage;

    public MainViewModel(DeviceViewModel deviceVm, FirmwareViewModel firmwareVm, RomViewModel romVm, SettingsViewModel settingsVm)
    {
        NavItems = new ObservableCollection<NavItem>
        {
            new("\uE8EA", "Device",   () => new DevicePage { DataContext = deviceVm }),
            new("\uE896", "Firmware", () => new FirmwarePage { DataContext = firmwareVm }),
            new("\uE721", "ROMs",     () => new RomsPage { DataContext = romVm }),
            new("\uE7B8", "Backup",   () => new BackupPage()),
            new("\uE90F", "Patch",    () => new PatchPage()),
            new("\uE945", "Flash",    () => new FlashPage()),
            new("\uE713", "Settings", () => new SettingsPage { DataContext = settingsVm }),
        };
        SelectedNavItem = NavItems[0];
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        CurrentPage = value?.ViewFactory();
    }
}
