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

        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash(args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) => { LogCrash(args.Exception); args.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, args) => { LogCrash(args.Exception); args.SetObserved(); };

        // Apply persisted theme before the first window opens so we don't flash Mocha when Latte is saved.
        Host.Theme.Apply(Host.SettingsStore.Settings.Theme);

        // First-run wizard runs modal. If the user closes it without completing, FirstRunCompleted stays
        // false and they'll see it again next launch — no harm done.
        if (!Host.SettingsStore.Settings.FirstRunCompleted)
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
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Devicer");
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
