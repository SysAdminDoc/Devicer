using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Devicer.Core.Models;
using Devicer.Core.Services;

namespace Devicer.App.ViewModels;

public partial class FirmwareViewModel : ObservableObject
{
    private readonly IFirmwareCheckService _firmware;
    private readonly Func<IFirmwareDownloadService> _downloadFactory;
    private CancellationTokenSource? _downloadCts;

    public ObservableCollection<FirmwareVersion> History { get; } = new();

    [ObservableProperty]
    private string? _model;

    [ObservableProperty]
    private string? _csc;

    [ObservableProperty]
    private string? _currentBuildId;

    [ObservableProperty]
    private string? _imei;

    [ObservableProperty]
    private FirmwareVersion? _latest;

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private string? _diagnostic;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private bool _updateAvailable;

    // ----- v0.3.1 download surface -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isDownloading;

    [ObservableProperty]
    private string? _downloadStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    [NotifyPropertyChangedFor(nameof(HasProgressFraction))]
    private double? _progressFraction;

    [ObservableProperty]
    private long _bytesProcessed;

    [ObservableProperty]
    private long? _totalBytes;

    [ObservableProperty]
    private string? _lastDownloadedPath;

    [ObservableProperty]
    private string? _lastDecryptedPath;

    public bool IsIdle => !IsDownloading && !IsChecking;
    public bool HasProgressFraction => ProgressFraction is not null;
    public double ProgressPercent => ProgressFraction is { } f ? f * 100.0 : 0.0;
    public string ProgressDisplay
    {
        get
        {
            if (TotalBytes is { } t && t > 0)
                return $"{FormatBytes(BytesProcessed)} / {FormatBytes(t)}";
            return BytesProcessed > 0 ? FormatBytes(BytesProcessed) : "—";
        }
    }

    public FirmwareViewModel(IFirmwareCheckService firmware, Func<IFirmwareDownloadService> downloadFactory)
    {
        _firmware = firmware;
        _downloadFactory = downloadFactory;
    }

    /// <summary>
    /// Pre-fill form from a probed device. Called when the Device tab's selection changes.
    /// </summary>
    public void PrefillFrom(DeviceInfo? device)
    {
        if (device is null) return;
        if (!string.IsNullOrWhiteSpace(device.Model)) Model = device.Model;
        if (!string.IsNullOrWhiteSpace(device.Csc)) Csc = device.Csc;
        // Use the Samsung PDA (AP firmware version) for comparison — NOT ro.build.id (Android build ID).
        var current = device.SamsungPda ?? device.BuildId;
        if (!string.IsNullOrWhiteSpace(current)) CurrentBuildId = current;
        if (!string.IsNullOrWhiteSpace(device.Imei)) Imei = device.Imei;

        // Reset latest so we don't show a stale match.
        Latest = null;
        UpdateAvailable = false;
        StatusText = null;
        Diagnostic = null;
    }

    [RelayCommand]
    public async Task CheckLatestAsync()
    {
        if (string.IsNullOrWhiteSpace(Model) || string.IsNullOrWhiteSpace(Csc))
        {
            Diagnostic = "Enter model + CSC, or select a device on the Device tab to autofill.";
            return;
        }

        IsChecking = true;
        OnPropertyChanged(nameof(IsIdle));
        Diagnostic = null;
        StatusText = "Querying Samsung OTA feed…";
        try
        {
            var result = await _firmware.GetLatestAsync(Model, Csc).ConfigureAwait(true);
            if (result is null)
            {
                StatusText = null;
                Diagnostic = $"Samsung returned no firmware feed for {Model} / {Csc}. The model+CSC pair may be invalid, or the device is too new for the public feed.";
                Latest = null;
                History.Clear();
                UpdateAvailable = false;
                return;
            }

            Latest = result.Latest;
            History.Clear();
            foreach (var v in result.UpgradeHistory) History.Add(v);

            UpdateAvailable = !string.IsNullOrWhiteSpace(CurrentBuildId)
                && !string.Equals(CurrentBuildId, Latest.Pda, StringComparison.OrdinalIgnoreCase)
                && FirmwareVersion.ComparePda(Latest.Pda, CurrentBuildId) > 0;

            StatusText = UpdateAvailable
                ? "Update available"
                : "You're on the latest";
        }
        catch (HttpRequestException ex)
        {
            StatusText = null;
            Diagnostic = $"Network error: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusText = null;
            Diagnostic = $"Lookup failed: {ex.Message}";
        }
        finally
        {
            IsChecking = false;
            OnPropertyChanged(nameof(IsIdle));
        }
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    public async Task DownloadLatestAsync()
    {
        if (Latest is null || string.IsNullOrWhiteSpace(Model) || string.IsNullOrWhiteSpace(Csc)) return;
        if (string.IsNullOrWhiteSpace(Imei) || Imei.Trim().Length < 14)
        {
            Diagnostic = "Samsung's FUS now requires a real device IMEI (14-15 digits). Connect the rooted device to auto-fill, or enter it manually below.";
            return;
        }

        IsDownloading = true;
        Diagnostic = null;
        DownloadStatus = "Starting…";
        ProgressFraction = null;
        BytesProcessed = 0;
        TotalBytes = null;
        OnPropertyChanged(nameof(ProgressDisplay));
        OnPropertyChanged(nameof(IsIdle));
        DownloadLatestCommand.NotifyCanExecuteChanged();
        CancelDownloadCommand.NotifyCanExecuteChanged();

        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;

        var progress = new Progress<FirmwareProgress>(p =>
        {
            DownloadStatus = p.Message ?? p.Phase.ToString();
            ProgressFraction = p.FractionComplete;
            BytesProcessed = p.BytesProcessed;
            TotalBytes = p.TotalBytes;
            OnPropertyChanged(nameof(ProgressDisplay));
        });

        var dl = _downloadFactory();
        try
        {
            var result = await dl.DownloadAndDecryptAsync(Model!, Csc!, Latest.Normalized, Imei!.Trim(), progress, ct).ConfigureAwait(true);
            LastDownloadedPath = result.EncryptedPath;
            LastDecryptedPath = result.DecryptedPath;
            DownloadStatus = $"Done. Decrypted to {result.DecryptedPath}";
        }
        catch (OperationCanceledException)
        {
            DownloadStatus = "Cancelled.";
        }
        catch (FusProtocolException ex)
        {
            Diagnostic = $"FUS protocol error: {ex.Message}";
            DownloadStatus = null;
        }
        catch (Exception ex)
        {
            Diagnostic = $"Download failed: {ex.Message}";
            DownloadStatus = null;
        }
        finally
        {
            IsDownloading = false;
            OnPropertyChanged(nameof(IsIdle));
            DownloadLatestCommand.NotifyCanExecuteChanged();
            CancelDownloadCommand.NotifyCanExecuteChanged();
            (dl as IDisposable)?.Dispose();
        }
    }

    private bool CanDownload() => Latest is not null && !IsDownloading && !IsChecking;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    private bool CanCancel() => IsDownloading;

    [RelayCommand(CanExecute = nameof(HasDecryptedPath))]
    public void OpenDownloadFolder()
    {
        var path = LastDecryptedPath ?? LastDownloadedPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        var folder = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Diagnostic = $"Could not open folder: {ex.Message}";
        }
    }

    private bool HasDecryptedPath() => !string.IsNullOrWhiteSpace(LastDecryptedPath) || !string.IsNullOrWhiteSpace(LastDownloadedPath);

    partial void OnLatestChanged(FirmwareVersion? value)
    {
        DownloadLatestCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsDownloadingChanged(bool value)
    {
        DownloadLatestCommand.NotifyCanExecuteChanged();
        CancelDownloadCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCheckingChanged(bool value)
    {
        DownloadLatestCommand.NotifyCanExecuteChanged();
    }

    partial void OnLastDecryptedPathChanged(string? value)
    {
        OpenDownloadFolderCommand.NotifyCanExecuteChanged();
    }

    partial void OnLastDownloadedPathChanged(string? value)
    {
        OpenDownloadFolderCommand.NotifyCanExecuteChanged();
    }

    private static string FormatBytes(long bytes)
    {
        const long k = 1024, m = k * 1024, g = m * 1024;
        return bytes switch
        {
            >= g => $"{bytes / (double)g:0.00} GB",
            >= m => $"{bytes / (double)m:0.0} MB",
            >= k => $"{bytes / (double)k:0.0} KB",
            _ => $"{bytes} B",
        };
    }
}
