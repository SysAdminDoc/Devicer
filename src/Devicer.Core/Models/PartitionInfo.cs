namespace Devicer.Core.Models;

/// <summary>
/// A block partition exposed by Android's <c>/dev/block/by-name</c> directory tree.
/// </summary>
public sealed record PartitionInfo
{
    /// <summary>By-name link (e.g. <c>efs</c>, <c>modem</c>, <c>boot_a</c>, <c>vbmeta</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Underlying block device path the symlink resolves to (e.g. <c>/dev/block/sdc4</c>).</summary>
    public required string BlockPath { get; init; }

    /// <summary>Size in bytes. Zero/Unknown if not probable.</summary>
    public long SizeBytes { get; init; }

    /// <summary>True if losing this partition can permanently brick the device or require board work.</summary>
    public bool IsCritical { get; init; }

    /// <summary>Short human reason this partition is flagged critical, or null when not.</summary>
    public string? CriticalReason { get; init; }

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
                _ => "—",
            };
        }
    }

    /// <summary>
    /// Names of partitions that, if lost or corrupted, can permanently brick the device's
    /// IMEI / cellular radio / secure storage. ALWAYS back up before any AP/CSC flash.
    /// </summary>
    public static readonly IReadOnlySet<string> CriticalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Samsung-critical: losing EFS bricks IMEI permanently.
        "efs", "efs1", "efs2", "factory", "factoryx",
        // Modem firmware / NV RAM — losing these breaks cellular.
        "modem", "modem1", "modem2", "nvram", "nv_data", "nv1", "nv2",
        // Qualcomm modem state.
        "modemst1", "modemst2", "fsg", "fsc",
        // Persistent root-of-trust / sensor calibration.
        "persist", "persist1", "persist2",
        // Carrier-locked / DRM keys.
        "drm", "carrier", "keystore", "keymaster",
    };

    public static string? ReasonFor(string name)
    {
        var n = name.ToLowerInvariant();
        return n switch
        {
            "efs" or "efs1" or "efs2" or "factory" or "factoryx" =>
                "Samsung EFS — IMEI / serial / NVRAM. Losing this bricks the radio permanently.",
            "modem" or "modem1" or "modem2" =>
                "Modem firmware. Recoverable but match-to-model required.",
            "nvram" or "nv_data" or "nv1" or "nv2" or "modemst1" or "modemst2" or "fsg" or "fsc" =>
                "Modem NVRAM / state. Loss breaks cellular calibration.",
            "persist" or "persist1" or "persist2" =>
                "Persistent root-of-trust + sensor calibration. Loss disables fingerprint, sensors, may trip Knox.",
            "drm" =>
                "DRM keystore. Loss disables Widevine L1.",
            "carrier" or "keystore" or "keymaster" =>
                "Carrier / hardware keystore. Loss may disable secure features.",
            _ => null,
        };
    }
}
