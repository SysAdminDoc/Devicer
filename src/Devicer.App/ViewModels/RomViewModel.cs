using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Devicer.App.Services;
using Devicer.Core.Models;
using Devicer.Core.Services;

namespace Devicer.App.ViewModels;

public partial class RomViewModel : ObservableObject
{
    private readonly IRomAggregatorService _aggregator;
    private readonly IRomDownloadService _downloader;

    private CancellationTokenSource? _dlCts;

    public ObservableCollection<RomEntry> Results { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial string? Codename { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial bool IsSearching { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CancelDownloadCommand))]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    public partial string? StatusText { get; set; }

    [ObservableProperty]
    public partial string? Diagnostic { get; set; }

    [ObservableProperty]
    public partial string? DownloadStatusText { get; set; }

    [ObservableProperty]
    public partial double? DownloadProgressFraction { get; set; }

    [ObservableProperty]
    public partial string? LastDownloadPath { get; set; }

    public bool HasResults => Results.Count > 0;

    public bool IsIdle => !IsSearching && !IsDownloading;

    public RomViewModel(IRomAggregatorService aggregator, IRomDownloadService downloader)
    {
        _aggregator = aggregator;
        _downloader = downloader;
        Results.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasResults));
    }

    public void PrefillFrom(DeviceInfo? device)
    {
        if (device is null) return;
        if (!string.IsNullOrWhiteSpace(device.Codename) && string.IsNullOrWhiteSpace(Codename))
            Codename = device.Codename;
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    public async Task SearchAsync()
    {
        var slug = Codename?.Trim();
        if (string.IsNullOrWhiteSpace(slug)) return;

        IsSearching = true;
        Diagnostic = null;
        StatusText = $"Searching LineageOS + crDroid for {slug}…";
        Results.Clear();
        try
        {
            var result = await _aggregator.SearchAsync(slug).ConfigureAwait(true);
            foreach (var e in result.Entries) Results.Add(e);

            if (result.Entries.Count == 0)
            {
                StatusText = null;
                Diagnostic = $"No builds published for codename '{slug}' on any indexed source. Verify the codename (lowercase, e.g. 'cheeseburger', 'alioth', 'pa3q'); the device may not be officially supported.";
            }
            else
            {
                var sources = string.Join(", ", result.SourcesWithResults.Select(s => s.ToString()));
                StatusText = $"{result.Entries.Count} build{(result.Entries.Count == 1 ? "" : "s")} from {sources}.";
            }
        }
        catch (HttpRequestException ex)
        {
            StatusText = null;
            Diagnostic = $"Network error: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusText = null;
            Diagnostic = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    private bool CanSearch() => !IsSearching && !string.IsNullOrWhiteSpace(Codename);

    [RelayCommand]
    public async Task DownloadRomAsync(RomEntry? entry)
    {
        if (entry is null || IsDownloading) return;

        IsDownloading = true;
        Diagnostic = null;
        DownloadStatusText = $"Downloading {entry.FileName}…";
        DownloadProgressFraction = null;
        LastDownloadPath = null;

        _dlCts?.Dispose();
        _dlCts = new CancellationTokenSource();
        var ct = _dlCts.Token;

        var progress = new Progress<RomDownloadProgress>(p =>
        {
            DownloadStatusText = p.Message ?? $"{p.Phase}";
            DownloadProgressFraction = p.FractionComplete;
        });

        try
        {
            var result = await _downloader.DownloadAsync(entry, progress, ct).ConfigureAwait(true);
            LastDownloadPath = Path.GetDirectoryName(result.LocalPath);

            if (result.HashAlgorithm is not null && !result.HashVerified)
            {
                Diagnostic = $"{result.HashAlgorithm} mismatch: expected {result.ExpectedHash}, got {result.ActualHash}. The file may be corrupt: re-download or verify manually.";
                DownloadStatusText = $"Downloaded but {result.HashAlgorithm} mismatch!";
            }
            else
            {
                var verifyNote = result.HashVerified ? $" ({result.HashAlgorithm} verified)" : "";
                DownloadStatusText = $"Saved to {result.LocalPath}{verifyNote}";
            }
        }
        catch (OperationCanceledException)
        {
            DownloadStatusText = "Download cancelled.";
            DownloadProgressFraction = null;
        }
        catch (HttpRequestException ex)
        {
            Diagnostic = $"Download failed: {ex.Message}";
            DownloadStatusText = null;
            DownloadProgressFraction = null;
        }
        catch (Exception ex)
        {
            Diagnostic = $"Download failed: {ex.Message}";
            DownloadStatusText = null;
            DownloadProgressFraction = null;
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelDownload))]
    public void CancelDownload() => _dlCts?.Cancel();

    private bool CanCancelDownload() => IsDownloading;

    [RelayCommand]
    public void OpenDownloadFolder()
    {
        if (string.IsNullOrWhiteSpace(LastDownloadPath) || !Directory.Exists(LastDownloadPath)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = LastDownloadPath, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Diagnostic = $"Could not open folder: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenInBrowser(RomEntry? entry)
    {
        if (entry is null) return;
        var err = UrlLauncher.TryOpen(entry.DownloadUrl);
        if (err is not null) Diagnostic = err;
    }

    [RelayCommand]
    public void OpenForum(RomEntry? entry)
    {
        if (entry?.ForumUrl is null) return;
        var err = UrlLauncher.TryOpen(entry.ForumUrl);
        if (err is not null) Diagnostic = err;
    }
}
