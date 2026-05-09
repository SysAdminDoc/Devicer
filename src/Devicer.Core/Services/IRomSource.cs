using Devicer.Core.Models;

namespace Devicer.Core.Services;

public interface IRomSource
{
    /// <summary>Identifier the aggregator uses to label and dedupe entries.</summary>
    RomSource Source { get; }

    /// <summary>
    /// Returns every ROM build the source publishes for <paramref name="codename"/>. Empty
    /// list if the source has no entries (404 / not yet supported); throws only on transport
    /// errors so the aggregator can flag them per-source without poisoning the whole result.
    /// </summary>
    Task<IReadOnlyList<RomEntry>> SearchAsync(string codename, CancellationToken ct = default);
}
