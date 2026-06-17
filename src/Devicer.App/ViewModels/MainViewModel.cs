using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Devicer.App.Views;

namespace Devicer.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // Cache the constructed UserControl per nav item so switching tabs preserves scroll
    // position, search results, IMEI dropdown selection, partition selection, etc. The
    // prior implementation rebuilt the page from its factory on every selection change,
    // which silently wiped any in-progress work the moment the user clicked a different
    // sidebar entry — even just to peek.
    private readonly Dictionary<NavItem, UserControl> _pageCache = new();

    public ObservableCollection<NavItem> NavItems { get; }

    [ObservableProperty]
    private NavItem? _selectedNavItem;

    [ObservableProperty]
    private UserControl? _currentPage;

    /// <summary>
    /// Pretty-printed version string for the sidebar (<c>v1.1.0</c>). Sourced from
    /// the executing assembly so it can never drift from the build the user is running.
    /// </summary>
    public string AppVersionDisplay { get; }

    public MainViewModel(DeviceViewModel deviceVm, FirmwareViewModel firmwareVm, RomViewModel romVm, BackupViewModel backupVm, PatchViewModel patchVm, FlashViewModel flashVm, UniversalViewModel universalVm, SettingsViewModel settingsVm)
    {
        NavItems = new ObservableCollection<NavItem>
        {
            new("\uE8EA", "Device",    () => new DevicePage { DataContext = deviceVm }),
            new("\uE896", "Firmware",  () => new FirmwarePage { DataContext = firmwareVm }),
            new("\uE721", "ROMs",      () => new RomsPage { DataContext = romVm }),
            new("\uE7B8", "Backup",    () => new BackupPage { DataContext = backupVm }),
            new("\uE90F", "Patch",     () => new PatchPage { DataContext = patchVm }),
            new("\uE945", "Flash",     () => new FlashPage { DataContext = flashVm }),
            new("\uE774", "Universal", () => new UniversalPage { DataContext = universalVm }),
            new("\uE713", "Settings",  () => new SettingsPage { DataContext = settingsVm }),
        };
        SelectedNavItem = NavItems[0];

        var asmVersion = Assembly.GetExecutingAssembly().GetName().Version;
        AppVersionDisplay = asmVersion is null ? "v?.?.?" : $"v{asmVersion.ToString(3)}";
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (value is null) { CurrentPage = null; return; }
        if (!_pageCache.TryGetValue(value, out var page))
        {
            page = value.ViewFactory();
            _pageCache[value] = page;
        }
        CurrentPage = page;
    }
}
