using Devicer.Core.Models;

namespace Devicer.Core.Services;

public sealed record RomSearchResult(
    IReadOnlyList<RomEntry> Entries,
    IReadOnlyList<RomSource> SourcesQueried,
    IReadOnlyList<RomSource> SourcesWithResults,
    string? Diagnostic = null);

public interface IRomAggregatorService
{
    /// <summary>
    /// Queries every registered ROM source in parallel, returns merged + sorted results.
    /// Per-source failures are isolated — one source going dark doesn't poison the rest.
    /// </summary>
    Task<RomSearchResult> SearchAsync(string codename, CancellationToken ct = default);
}

public sealed class RomAggregatorService : IRomAggregatorService, IDisposable
{
    private readonly IReadOnlyList<IRomSource> _sources;
    private readonly bool _ownsSources;

    public RomAggregatorService(IEnumerable<IRomSource>? sources = null)
    {
        // We only own the default sources we constructed ourselves. Disposing externally
        // supplied IRomSource instances would yank HttpClients out from under whoever
        // built them — common pattern when tests inject mocks or when a future caller
        // pools sources across services.
        _ownsSources = sources is null;
        _sources = (sources ?? new IRomSource[]
        {
            new LineageOsRomSource(),
            new CrDroidRomSource(),
        }).ToList();
    }

    public async Task<RomSearchResult> SearchAsync(string codename, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(codename))
            return new RomSearchResult(Array.Empty<RomEntry>(), Array.Empty<RomSource>(), Array.Empty<RomSource>(), "Codename is required.");

        var queried = _sources.Select(s => s.Source).ToList();
        var tasks = _sources.Select(s => SafeSearchAsync(s, codename, ct)).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var merged = new List<RomEntry>();
        var withResults = new List<RomSource>();
        for (int i = 0; i < _sources.Count; i++)
        {
            var (entries, _) = results[i];
            if (entries.Count > 0)
            {
                merged.AddRange(entries);
                withResults.Add(_sources[i].Source);
            }
        }

        // Sort newest-first across sources so the latest build of any ROM is always at the top.
        merged.Sort((a, b) => b.BuildDate.CompareTo(a.BuildDate));

        return new RomSearchResult(merged, queried, withResults);
    }

    private static async Task<(IReadOnlyList<RomEntry> entries, Exception? error)> SafeSearchAsync(IRomSource src, string codename, CancellationToken ct)
    {
        try
        {
            var entries = await src.SearchAsync(codename, ct).ConfigureAwait(false);
            return (entries, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return (Array.Empty<RomEntry>(), ex);
        }
    }

    public void Dispose()
    {
        if (!_ownsSources) return;
        foreach (var s in _sources)
            (s as IDisposable)?.Dispose();
    }
}
