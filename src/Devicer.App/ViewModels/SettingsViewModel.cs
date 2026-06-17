using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Devicer.App.Services;
using Devicer.Core.Services;

namespace Devicer.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsStore _store;
    private readonly ThemeManager _theme;
    private readonly IAdbService _adb;
    private readonly IFastbootService _fastboot;

    public IReadOnlyList<AppTheme> Themes { get; } = new[] { AppTheme.Mocha, AppTheme.Latte };

    [ObservableProperty]
    private AppTheme _selectedTheme;

    [ObservableProperty]
    private int _probeIntervalSeconds;

    [ObservableProperty]
    private string _adbStatus = "Checking…";

    [ObservableProperty]
    private bool _adbVersionWarning;

    [ObservableProperty]
    private string _fastbootStatus = "Checking…";

    public string AppVersion { get; }
    public string SettingsPath { get; }
    public string CrashlogPath { get; }
    public string ToolsCachePath { get; }
    public string? SettingsSaveError => _store.LastSaveError;

    public SettingsViewModel(AppSettingsStore store, ThemeManager theme, IAdbService adb, IFastbootService fastboot)
    {
        _store = store;
        _theme = theme;
        _adb = adb;
        _fastboot = fastboot;

        _selectedTheme = store.Settings.Theme;
        _probeIntervalSeconds = store.Settings.ProbeIntervalSeconds;

        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Devicer");
        AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        SettingsPath = store.SettingsPath;
        CrashlogPath = Path.Combine(dataDir, "crashlog.txt");
        ToolsCachePath = Path.Combine(dataDir, "tools");

        _ = RefreshToolStatusAsync();
    }

    [RelayCommand]
    public async Task RefreshToolStatusAsync()
    {
        AdbStatus = "Checking…";
        FastbootStatus = "Checking…";
        AdbVersionWarning = false;
        var adbOk = await _adb.IsAvailableAsync();
        var fastbootOk = await _fastboot.IsAvailableAsync();

        if (adbOk)
        {
            var ver = await _adb.GetVersionAsync();
            if (ver is not null)
            {
                AdbStatus = $"v{ver} on PATH";
                if (Version.TryParse(ver, out var parsed) && parsed < IAdbService.MinSafeVersion)
                {
                    AdbStatus = $"v{ver} on PATH — OUTDATED (< 36.0.2, has Samsung detection + file truncation bugs)";
                    AdbVersionWarning = true;
                }
            }
            else
                AdbStatus = "Available on PATH (version unknown)";
        }
        else
            AdbStatus = "Not found — install Android SDK Platform-Tools v37+";

        FastbootStatus = fastbootOk ? "Available on PATH" : "Not found — install Android SDK Platform-Tools v37+";
    }

    [ObservableProperty]
    private string? _openFolderError;

    [RelayCommand]
    public void OpenSettingsFolder()
    {
        OpenFolderError = null;
        var dir = Path.GetDirectoryName(SettingsPath);
        if (dir is null || !Directory.Exists(dir))
        {
            OpenFolderError = $"Folder not found: {dir ?? "(none)"}";
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            // Shell-launch can fail when no Explorer / file-association / shell handler
            // is registered (terminal-only Windows IoT, locked-down enterprise builds,
            // ServerCore SKUs). Surface the error to the UI rather than crashing the
            // dispatcher.
            OpenFolderError = $"Could not open folder: {ex.Message}";
        }
    }

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        _theme.Apply(value);
    }

    partial void OnProbeIntervalSecondsChanged(int value)
    {
        var clamped = Math.Clamp(value, 2, 30);
        if (clamped != value)
        {
            _probeIntervalSeconds = clamped;
            OnPropertyChanged(nameof(ProbeIntervalSeconds));
        }
        _store.Settings.ProbeIntervalSeconds = clamped;
        _store.Save();
        ProbeIntervalChanged?.Invoke(this, clamped);
    }

    public event EventHandler<int>? ProbeIntervalChanged;
}
