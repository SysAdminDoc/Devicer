namespace Devicer.Core.Models;

/// <summary>One partition image inside a backup set.</summary>
public sealed record PartitionBackupEntry
{
    public required string Name { get; init; }
    public required string FileName { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }
    public bool IsCritical { get; init; }
}

/// <summary>
/// Versioned backup catalog entry — describes one backup run. Persisted alongside the
/// image files at <c>%LOCALAPPDATA%\Devicer\backups\&lt;serial&gt;\&lt;timestamp&gt;\manifest.json</c>.
/// </summary>
public sealed record BackupManifest
{
    public required string Serial { get; init; }
    public required string? Model { get; init; }
    public required string? Codename { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required IReadOnlyList<PartitionBackupEntry> Partitions { get; init; }

    /// <summary>Human-readable label combining model + creation time, e.g. <c>SM-S938B / 2026-05-09 12:34</c>.</summary>
    public string DisplayName => $"{Model ?? Serial} / {CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";

    public long TotalBytes
    {
        get { long n = 0; foreach (var p in Partitions) n += p.SizeBytes; return n; }
    }
}
