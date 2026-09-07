namespace Devicer.Core.Models;

/// <summary>
/// One file inside an Odin firmware tar (PDA/AP/CSC/CP/HOME_CSC/BL packages).
/// Examples: <c>boot.img.lz4</c>, <c>system.img.lz4</c>, <c>cache.img.lz4</c>, <c>vbmeta.img.lz4</c>.
/// </summary>
public sealed record OdinTarEntry
{
    public required string Name { get; init; }
    public required long SizeBytes { get; init; }

    /// <summary>True if the entry name suggests it's a partition image we'd flash.</summary>
    public bool IsImage => Name.Contains(".img", StringComparison.OrdinalIgnoreCase)
                        || Name.Contains(".bin", StringComparison.OrdinalIgnoreCase)
                        || Name.Contains(".lz4", StringComparison.OrdinalIgnoreCase);

    /// <summary>The partition target derived from the file name (e.g. <c>boot</c> from <c>boot.img.lz4</c>).</summary>
    public string PartitionGuess
    {
        get
        {
            var n = Name;
            // Strip directory and extension chain.
            var slash = n.LastIndexOf('/');
            if (slash >= 0) n = n[(slash + 1)..];
            // Strip .lz4 / .img / .bin extensions.
            foreach (var ext in new[] { ".lz4", ".img", ".bin" })
                if (n.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    n = n[..^ext.Length];
            return n.ToLowerInvariant();
        }
    }

    public string SizeDisplay
    {
        get
        {
            const long k = 1024, m = k * 1024, g = m * 1024;
            return SizeBytes switch
            {
                >= g => $"{SizeBytes / (double)g:0.00} GB",
                >= m => $"{SizeBytes / (double)m:0.0} MB",
                >= k => $"{SizeBytes / (double)k:0.0} KB",
                > 0 => $"{SizeBytes} B",
                _ => "N/A",
            };
        }
    }
}

public sealed record OdinTarInfo
{
    public required string Path { get; init; }
    public required string FileName { get; init; }
    public required long FileSize { get; init; }
    public required IReadOnlyList<OdinTarEntry> Entries { get; init; }

    /// <summary>True if the file ended in <c>.tar.md5</c>; the inner tar is the same, with MD5 appended.</summary>
    public required bool HasMd5Suffix { get; init; }

    /// <summary>The package class: AP / CP / CSC / HOME_CSC / BL: derived from the FILE name.</summary>
    public string? PackageHint
    {
        get
        {
            var name = FileName.ToUpperInvariant();
            if (name.StartsWith("AP_")) return "AP";
            if (name.StartsWith("BL_")) return "BL";
            if (name.StartsWith("CP_")) return "CP";
            if (name.StartsWith("HOME_CSC_")) return "HOME_CSC";
            if (name.StartsWith("CSC_")) return "CSC";
            return null;
        }
    }
}
