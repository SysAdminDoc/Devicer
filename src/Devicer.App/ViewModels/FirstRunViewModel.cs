using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Devicer.App.Services;
using Devicer.Core.Services;

namespace Devicer.App.ViewModels;

public partial class FirstRunViewModel : ObservableObject
{
    private readonly AppSettingsStore _store;
    private readonly IAdbService _adb;
    private readonly IFastbootService _fastboot;

    [ObservableProperty]
    private bool _isChecking = true;

    [ObservableProperty]
    private bool? _adbAvailable;

    [ObservableProperty]
    private bool? _fastbootAvailable;

    [ObservableProperty]
    private bool _everythingReady;

    public string AdbStatusText => AdbAvailable switch
    {
        true => "adb detected on PATH",
        false => "adb not found — install Android SDK Platform-Tools v37+ and add it to PATH",
        _ => "Checking adb…",
    };

    public string FastbootStatusText => FastbootAvailable switch
    {
        true => "fastboot detected on PATH",
        false => "fastboot not found — install Android SDK Platform-Tools v37+ and add it to PATH",
        _ => "Checking fastboot…",
    };

    public FirstRunViewModel(AppSettingsStore store, IAdbService adb, IFastbootService fastboot)
    {
        _store = store;
        _adb = adb;
        _fastboot = fastboot;
        _ = CheckAsync();
    }

    [RelayCommand]
    public async Task CheckAsync()
    {
        IsChecking = true;
        AdbAvailable = null;
        FastbootAvailable = null;
        OnPropertyChanged(nameof(AdbStatusText));
        OnPropertyChanged(nameof(FastbootStatusText));

        AdbAvailable = await _adb.IsAvailableAsync();
        FastbootAvailable = await _fastboot.IsAvailableAsync();
        EverythingReady = AdbAvailable == true && FastbootAvailable == true;

        OnPropertyChanged(nameof(AdbStatusText));
        OnPropertyChanged(nameof(FastbootStatusText));
        IsChecking = false;
    }

    [RelayCommand]
    public void Complete()
    {
        _store.Settings.FirstRunCompleted = true;
        _store.Save();
    }
}
