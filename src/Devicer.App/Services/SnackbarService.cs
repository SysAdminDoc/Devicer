using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Devicer.App.Services;

public enum SnackbarSeverity { Info, Success, Warning, Error }

public partial class SnackbarService : ObservableObject
{
    private DispatcherTimer? _timer;

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial SnackbarSeverity Severity { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    public void Show(string message, SnackbarSeverity severity = SnackbarSeverity.Info, int durationMs = 5000)
    {
        Message = message;
        Severity = severity;
        IsVisible = true;

        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
        _timer.Tick += (_, _) => { Dismiss(); _timer.Stop(); };
        _timer.Start();
    }

    public void Dismiss()
    {
        IsVisible = false;
        _timer?.Stop();
    }
}
