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
    public partial bool IsChecking { get; set; } = true;

    [ObservableProperty]
    public partial bool? AdbAvailable { get; set; }

    [ObservableProperty]
    public partial bool? FastbootAvailable { get; set; }

    [ObservableProperty]
    public partial bool EverythingReady { get; set; }

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
        // Fire-and-forget: any synchronous throw inside the async method would otherwise
        // bubble straight out of the constructor; the wrapper task observes the failure.
        _ = SafeCheckAsync();
    }

    private async Task SafeCheckAsync()
    {
        try { await CheckAsync().ConfigureAwait(true); }
        catch
        {
            // Render the row as unavailable rather than crashing the wizard. The user can
            // still hit Re-check; the underlying adb call retries cleanly.
            AdbAvailable ??= false;
            FastbootAvailable ??= false;
            IsChecking = false;
            OnPropertyChanged(nameof(AdbStatusText));
            OnPropertyChanged(nameof(FastbootStatusText));
        }
    }

    [RelayCommand]
    public async Task CheckAsync()
    {
        IsChecking = true;
        AdbAvailable = null;
        FastbootAvailable = null;
        OnPropertyChanged(nameof(AdbStatusText));
        OnPropertyChanged(nameof(FastbootStatusText));

        try
        {
            AdbAvailable = await _adb.IsAvailableAsync().ConfigureAwait(true);
            FastbootAvailable = await _fastboot.IsAvailableAsync().ConfigureAwait(true);
        }
        catch
        {
            AdbAvailable ??= false;
            FastbootAvailable ??= false;
        }
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
