namespace Devicer.Core.Models;

public enum RomSource
{
    LineageOS,
    CrDroid,
    PixelExperience,
    EvolutionX,
    Other,
}

public enum RomKind
{
    Stable,
    Monthly,
    Weekly,
    Nightly,
    Alpha,
    Beta,
    Unknown,
}

public sealed record RomEntry
{
    public required RomSource Source { get; init; }
    public required RomKind Kind { get; init; }
    public required string Codename { get; init; }
    public required string Version { get; init; }
    public DateTimeOffset BuildDate { get; init; }
    public long SizeBytes { get; init; }
    public required string FileName { get; init; }
    public required Uri DownloadUrl { get; init; }
    public string? Sha256 { get; init; }
    public string? Md5 { get; init; }
    public string? Maintainer { get; init; }
    public Uri? ForumUrl { get; init; }

    /// <summary>Best-effort human size, e.g. "1.0 GB".</summary>
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
                _ => $"{SizeBytes} B",
            };
        }
    }

    public string SourceDisplay => Source switch
    {
        RomSource.LineageOS => "LineageOS",
        RomSource.CrDroid => "crDroid",
        RomSource.PixelExperience => "PixelExperience",
        RomSource.EvolutionX => "Evolution X",
        _ => "Other",
    };

    public string KindDisplay => Kind switch
    {
        RomKind.Stable => "Stable",
        RomKind.Monthly => "Monthly",
        RomKind.Weekly => "Weekly",
        RomKind.Nightly => "Nightly",
        RomKind.Alpha => "Alpha",
        RomKind.Beta => "Beta",
        _ => "—",
    };
}
