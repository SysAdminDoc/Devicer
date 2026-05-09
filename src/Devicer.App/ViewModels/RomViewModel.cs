using System.Collections.ObjectModel;
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

    public ObservableCollection<RomEntry> Results { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string? _codename;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private bool _isSearching;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private string? _diagnostic;

    /// <summary>True if at least one source returned results.</summary>
    public bool HasResults => Results.Count > 0;

    public bool IsIdle => !IsSearching;

    public RomViewModel(IRomAggregatorService aggregator)
    {
        _aggregator = aggregator;
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
    public void OpenDownload(RomEntry? entry)
    {
        if (entry is null) return;
        // Route through UrlLauncher: ROM-feed URLs come from a third-party JSON
        // (LineageOS / crDroid maintainers' repos) and a compromised feed could otherwise
        // ship a `file:///c:/payload.exe` that ShellExecute would happily invoke.
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
