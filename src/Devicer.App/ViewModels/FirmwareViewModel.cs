using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Devicer.App.Services;
using Devicer.Core.Models;
using Devicer.Core.Services;

namespace Devicer.App.ViewModels;

public sealed class FirmwareRegionResultItem
{
    public FirmwareRegionResultItem(RegionalFirmwareResult result, string? currentBuildId)
    {
        Csc = result.Csc;
        Latest = result.Firmware?.Latest;
        UpgradeHistory = result.Firmware?.UpgradeHistory ?? Array.Empty<FirmwareVersion>();
        Error = result.Error;

        if (Latest is null)
        {
            StatusText = string.IsNullOrWhiteSpace(Error) ? "No firmware feed" : $"Lookup failed: {Error}";
            return;
        }

        if (string.IsNullOrWhiteSpace(currentBuildId))
        {
            StatusText = "Latest feed found";
            return;
        }

        var diff = FirmwareVersion.ComparePda(Latest.Pda, currentBuildId);
        UpdateAvailable = diff > 0;
        StatusText = diff > 0
            ? "Update available"
            : diff < 0
                ? "Installed PDA is newer"
                : "Current";
    }

    public string Csc { get; }
    public FirmwareVersion? Latest { get; }
    public IReadOnlyList<FirmwareVersion> UpgradeHistory { get; }
    public string? Error { get; }
    public bool HasFirmware => Latest is not null;
    public bool UpdateAvailable { get; }
    public string StatusText { get; }
    public string LatestPda => Latest?.Pda ?? "—";
    public string LatestCsc => Latest?.Csc ?? "—";
    public string LatestCp => Latest?.Cp ?? "—";
    public string LatestBoot => Latest?.Boot ?? "—";
}

public partial class FirmwareViewModel : ObservableObject
{
    private readonly IFirmwareCheckService _firmware;
    private readonly Func<IFirmwareDownloadService> _downloadFactory;
    private readonly IAdbService? _adb;
    private readonly ImeiCache? _imeiCache;
    private CancellationTokenSource? _downloadCts;
    private string? _serial;

    public ObservableCollection<FirmwareVersion> History { get; } = new();
    public ObservableCollection<FirmwareRegionResultItem> RegionResults { get; } = new();

    [ObservableProperty]
    private string? _model;

    [ObservableProperty]
    private string? _csc;

    [ObservableProperty]
    private string? _currentBuildId;

    [ObservableProperty]
    private string? _imei;

    /// <summary>
    /// Bound to the ComboBox's <c>SelectedItem</c>. When the user picks an item from the
    /// dropdown, this setter copies the entry's IMEI digits into the editable text field.
    /// </summary>
    [ObservableProperty]
    private ImeiCacheEntry? _selectedImeiEntry;

    partial void OnSelectedImeiEntryChanged(ImeiCacheEntry? value)
    {
        if (value is null) return;
        Imei = value.Imei;
    }

    [ObservableProperty]
    private FirmwareVersion? _latest;

    [ObservableProperty]
    private FirmwareRegionResultItem? _selectedRegionResult;

    partial void OnSelectedRegionResultChanged(FirmwareRegionResultItem? value)
    {
        Latest = value?.Latest;
        History.Clear();
        if (value is not null)
        {
            foreach (var v in value.UpgradeHistory) History.Add(v);
        }

        UpdateAvailable = value?.UpdateAvailable ?? false;
        StatusText = value?.HasFirmware == true ? value.StatusText : null;
        DownloadLatestCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private string? _diagnostic;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpToDateBadge))]
    private string? _statusText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpToDateBadge))]
    private bool _updateAvailable;

    /// <summary>
    /// Visibility helper for the green "you're on the latest" badge. The badge had been
    /// gated on <c>StatusText != null</c> alone, which let it render side-by-side with
    /// the orange <c>UpdateAvailable</c> badge whenever both conditions held — both badges
    /// then sat on the right with conflicting messaging. Show the success badge only when
    /// we actually have status text AND there's no pending update.
    /// </summary>
    public bool ShowUpToDateBadge => !UpdateAvailable && !string.IsNullOrWhiteSpace(StatusText);

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
    [NotifyPropertyChangedFor(nameof(HasGeofenceFailover))]
    private bool _showMirrorFailover;

    public ObservableCollection<FirmwareMirror> Mirrors { get; } = new(FirmwareMirrors.All);

    public bool HasGeofenceFailover => ShowMirrorFailover && Mirrors.Count > 0;

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

    public ObservableCollection<ImeiCacheEntry> ImeiHistory { get; } = new();

    public FirmwareViewModel(IFirmwareCheckService firmware, Func<IFirmwareDownloadService> downloadFactory, IAdbService? adb = null, ImeiCache? imeiCache = null)
    {
        _firmware = firmware;
        _downloadFactory = downloadFactory;
        _adb = adb;
        _imeiCache = imeiCache;
        RefreshImeiHistory();
    }

    private void RefreshImeiHistory()
    {
        ImeiHistory.Clear();
        if (_imeiCache is null) return;
        foreach (var e in _imeiCache.Entries) ImeiHistory.Add(e);
    }

    /// <summary>
    /// Pre-fill form from a probed device. Called when the Device tab's selection changes.
    /// </summary>
    public void PrefillFrom(DeviceInfo? device)
    {
        if (device is null) return;
        _serial = device.Serial;
        if (!string.IsNullOrWhiteSpace(device.Model)) Model = device.Model;
        if (!string.IsNullOrWhiteSpace(device.Csc)) Csc = device.Csc;
        // Use the Samsung PDA (AP firmware version) for comparison — NOT ro.build.id (Android build ID).
        var current = device.SamsungPda ?? device.BuildId;
        if (!string.IsNullOrWhiteSpace(current)) CurrentBuildId = current;
        if (!string.IsNullOrWhiteSpace(device.Imei)) Imei = device.Imei;
        ShowImeiOnPhoneCommand.NotifyCanExecuteChanged();

        // Reset latest so we don't show a stale match.
        Latest = null;
        RegionResults.Clear();
        SelectedRegionResult = null;
        UpdateAvailable = false;
        StatusText = null;
        Diagnostic = null;
    }

    [RelayCommand]
    public async Task CheckLatestAsync()
    {
        var cscs = FirmwareCheckService.ParseCscList(Csc);
        if (string.IsNullOrWhiteSpace(Model) || cscs.Count == 0)
        {
            Diagnostic = "Enter model + one or more CSCs, or select a device on the Device tab to autofill.";
            return;
        }

        IsChecking = true;
        OnPropertyChanged(nameof(IsIdle));
        Diagnostic = null;
        StatusText = cscs.Count == 1
            ? "Querying Samsung OTA feed…"
            : $"Querying {cscs.Count} Samsung OTA feeds…";
        try
        {
            var results = await _firmware.GetLatestAcrossRegionsAsync(Model, cscs).ConfigureAwait(true);
            RegionResults.Clear();
            foreach (var result in results)
                RegionResults.Add(new FirmwareRegionResultItem(result, CurrentBuildId));

            SelectedRegionResult = RegionResults.FirstOrDefault(r => r.UpdateAvailable)
                ?? RegionResults.FirstOrDefault(r => r.HasFirmware)
                ?? RegionResults.FirstOrDefault();

            if (SelectedRegionResult?.HasFirmware != true)
            {
                StatusText = null;
                var detail = string.Join("; ", RegionResults.Select(r => $"{r.Csc}: {r.StatusText}"));
                Diagnostic = $"Samsung returned no firmware feed for {Model} / {string.Join(", ", cscs)}. The model+CSC pair may be invalid, or the device is too new for the public feed."
                    + (string.IsNullOrWhiteSpace(detail) ? string.Empty : $" Details: {detail}");
                Latest = null;
                History.Clear();
                UpdateAvailable = false;
                return;
            }

            var misses = RegionResults
                .Where(r => !r.HasFirmware)
                .Select(r => $"{r.Csc}: {r.StatusText}")
                .ToArray();
            Diagnostic = misses.Length > 0
                ? $"Some CSC feeds did not return firmware: {string.Join("; ", misses)}"
                : null;
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
        var downloadCsc = GetActiveCsc();
        if (Latest is null || string.IsNullOrWhiteSpace(Model) || string.IsNullOrWhiteSpace(downloadCsc)) return;

        var imeiCandidate = (Imei ?? string.Empty).Trim();
        // Strip whitespace and dashes the user might have pasted from a phone-info screen
        // (e.g. "354 237 929 314 284" or "354-237-929-314-284") so we don't fail their
        // perfectly valid IMEI for cosmetic reasons.
        imeiCandidate = new string(imeiCandidate.Where(c => c is not ' ' and not '-').ToArray());
        if (imeiCandidate.Length is < 14 or > 15 || imeiCandidate.Any(c => c < '0' || c > '9'))
        {
            Diagnostic = "Samsung's FUS now requires a real device IMEI (14-15 digits, numeric only). Connect the rooted device to auto-fill, or enter it manually below.";
            return;
        }
        // Normalize the field so the value the user sees matches what we're actually sending.
        Imei = imeiCandidate;

        // Reset prior failover banner — only show if THIS attempt geofences.
        ShowMirrorFailover = false;

        // Persist the IMEI so the user doesn't have to retype 15 digits on every retry.
        // Pair it with the model+CSC it's being used with so the dropdown can label entries.
        _imeiCache?.AddOrTouch(imeiCandidate, Model, downloadCsc);
        RefreshImeiHistory();

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
            var result = await dl.DownloadAndDecryptAsync(Model!, downloadCsc, Latest.Normalized, imeiCandidate, progress, ct).ConfigureAwait(true);
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
            var friendly = FusErrorClassifier.Classify(ex, downloadCsc);
            var sb = new System.Text.StringBuilder();
            sb.Append("⚠  ").AppendLine(friendly.Title);
            sb.AppendLine();
            sb.AppendLine(friendly.Explanation);
            sb.AppendLine();
            sb.AppendLine("What to do:");
            sb.AppendLine(friendly.SuggestedAction);
            if (!string.IsNullOrWhiteSpace(friendly.TechnicalDetail))
            {
                sb.AppendLine();
                sb.AppendLine("— Technical detail —");
                sb.AppendLine(friendly.TechnicalDetail);
            }
            sb.AppendLine();
            sb.Append("Full protocol log: ").Append(DevicerLog.LogPath);
            Diagnostic = sb.ToString();
            DownloadStatus = null;

            // For geofence failures, surface the public-mirror failover row so the user
            // can pivot to a non-region-locked download without leaving Devicer.
            ShowMirrorFailover = friendly.IsGeofence
                && !string.IsNullOrWhiteSpace(Model)
                && !string.IsNullOrWhiteSpace(downloadCsc);
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

    private bool CanDownload() => Latest is not null && !IsDownloading && !IsChecking && !string.IsNullOrWhiteSpace(GetActiveCsc());

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    private bool CanCancel() => IsDownloading;

    [RelayCommand]
    public void RemoveImeiHistory(ImeiCacheEntry? entry)
    {
        if (entry is null || _imeiCache is null) return;
        _imeiCache.Remove(entry.Imei);
        RefreshImeiHistory();
    }

    [RelayCommand]
    public void OpenMirror(FirmwareMirror? mirror)
    {
        var activeCsc = GetActiveCsc();
        if (mirror is null || string.IsNullOrWhiteSpace(Model) || string.IsNullOrWhiteSpace(activeCsc)) return;
        var url = mirror.BuildUrl(Model!, activeCsc);
        var err = UrlLauncher.TryOpen(url);
        if (err is not null) Diagnostic = $"Could not open {mirror.Name}: {err}";
    }

    [RelayCommand(CanExecute = nameof(CanShowImei))]
    public async Task ShowImeiOnPhoneAsync()
    {
        if (_adb is null || string.IsNullOrWhiteSpace(_serial)) return;
        try
        {
            var ok = await _adb.ShowImeiOnPhoneAsync(_serial!).ConfigureAwait(true);
            Diagnostic = ok
                ? "Opened the About phone screen on your device. Tap 'Status information' (or 'IMEI'), copy the 15-digit IMEI 1, and paste it into the IMEI field above."
                : "Could not open the About phone screen. Manually navigate Settings → About phone → Status information → IMEI.";
        }
        catch (Exception ex)
        {
            Diagnostic = $"Could not trigger IMEI dialog: {ex.Message}";
        }
    }

    private bool CanShowImei() => _adb is not null && !string.IsNullOrWhiteSpace(_serial);

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

    private string? GetActiveCsc() =>
        SelectedRegionResult?.HasFirmware == true
            ? SelectedRegionResult.Csc
            : FirmwareCheckService.ParseCscList(Csc).FirstOrDefault();

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
