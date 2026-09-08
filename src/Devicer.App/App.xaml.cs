using System.IO;
using System.Windows;
using System.Windows.Threading;
using Devicer.App.Services;
using Devicer.App.ViewModels;
using Devicer.App.Views;

namespace Devicer.App;

public partial class App : Application
{
    public static AppHost Host { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var isMarketingCapture = MarketingCaptureMode.IsEnabled;

        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash(args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) => { LogCrash(args.Exception); args.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, args) => { LogCrash(args.Exception); args.SetObserved(); };

        if (!isMarketingCapture && !Host.SettingsStore.Settings.FirstRunCompleted)
        {
            var systemTheme = ThemeManager.DetectSystemTheme();
            Host.SettingsStore.Settings.Theme = systemTheme;
        }
        Host.Theme.Apply(isMarketingCapture ? AppTheme.Mocha : Host.SettingsStore.Settings.Theme);

        if (!isMarketingCapture && !Host.SettingsStore.Settings.FirstRunCompleted)
        {
            var firstRunVm = new FirstRunViewModel(Host.SettingsStore, Host.Adb, Host.Fastboot);
            var firstRun = new FirstRunWindow(firstRunVm);
            firstRun.ShowDialog();
        }

        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }

    private static void LogCrash(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var dir = MarketingCaptureMode.DataDirectory;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "crashlog.txt");
            File.AppendAllText(path,
                $"[{DateTime.Now:O}] {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // swallow logging errors
        }
    }
}
