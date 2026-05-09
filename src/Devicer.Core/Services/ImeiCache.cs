using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Devicer.Core.Services;

/// <summary>
/// One persistent IMEI entry. The model/CSC the IMEI was last paired with is captured
/// alongside so the dropdown can label entries (e.g. "354237929314284 — SM-S938B/EUX").
/// </summary>
public sealed record ImeiCacheEntry
{
    public required string Imei { get; init; }
    public string? Model { get; init; }
    public string? Csc { get; init; }
    public DateTimeOffset LastUsedUtc { get; init; } = DateTimeOffset.UtcNow;

    public string DisplayLabel
    {
        get
        {
            var ctx = !string.IsNullOrWhiteSpace(Model) || !string.IsNullOrWhiteSpace(Csc)
                ? $"  —  {Model}{(string.IsNullOrWhiteSpace(Csc) ? "" : $" / {Csc}")}"
                : string.Empty;
            return $"{Imei}{ctx}";
        }
    }
}

/// <summary>
/// JSON-backed IMEI cache at <c>%LOCALAPPDATA%\Devicer\imei-cache.json</c>. Saves every
/// IMEI a user commits to a download, so they don't have to retype 15 digits next time.
/// Entries are sorted newest-used-first; the cache is capped at <see cref="MaxEntries"/>
/// to keep the dropdown short.
/// </summary>
public sealed class ImeiCache
{
    public const int MaxEntries = 20;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly object _lock = new();
    private List<ImeiCacheEntry> _entries;

    public ImeiCache(string? path = null)
    {
        if (path is null)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Devicer");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "imei-cache.json");
        }
        else
        {
            _path = path;
        }
        _entries = Load();
    }

    public string CachePath => _path;

    public IReadOnlyList<ImeiCacheEntry> Entries
    {
        get { lock (_lock) return _entries.ToList(); }
    }

    /// <summary>
    /// Adds or updates an IMEI in the cache. If the IMEI already exists its model+CSC
    /// are refreshed and its LastUsedUtc bumped to now; otherwise it's prepended. Cache
    /// is trimmed to <see cref="MaxEntries"/> by oldest-LastUsedUtc.
    /// </summary>
    public void AddOrTouch(string imei, string? model = null, string? csc = null)
    {
        if (string.IsNullOrWhiteSpace(imei)) return;
        var trimmed = imei.Trim();
        // Real IMEIs are exactly 14 (no check digit) or 15 (with). Allowing 16 was a
        // typo trap — a 16-digit pasted ICCID would silently cache and the user would
        // pick it later expecting an IMEI. Reject anything outside that range AND reject
        // non-digits (an IMEI never contains letters, spaces, or dashes).
        if (trimmed.Length is < 14 or > 15) return;
        foreach (var c in trimmed)
            if (c < '0' || c > '9') return;

        lock (_lock)
        {
            var existing = _entries.FindIndex(e => string.Equals(e.Imei, trimmed, StringComparison.Ordinal));
            var entry = new ImeiCacheEntry
            {
                Imei = trimmed,
                Model = string.IsNullOrWhiteSpace(model) ? (existing >= 0 ? _entries[existing].Model : null) : model,
                Csc = string.IsNullOrWhiteSpace(csc) ? (existing >= 0 ? _entries[existing].Csc : null) : csc,
                LastUsedUtc = DateTimeOffset.UtcNow,
            };
            if (existing >= 0) _entries.RemoveAt(existing);
            _entries.Insert(0, entry);
            if (_entries.Count > MaxEntries) _entries = _entries.Take(MaxEntries).ToList();
            Save();
        }
    }

    /// <summary>Remove a specific IMEI from the cache.</summary>
    public bool Remove(string imei)
    {
        if (string.IsNullOrWhiteSpace(imei)) return false;
        var trimmed = imei.Trim();
        lock (_lock)
        {
            var idx = _entries.FindIndex(e => string.Equals(e.Imei, trimmed, StringComparison.Ordinal));
            if (idx < 0) return false;
            _entries.RemoveAt(idx);
            Save();
            return true;
        }
    }

    private List<ImeiCacheEntry> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new List<ImeiCacheEntry>();
            var json = File.ReadAllText(_path);
            var list = JsonSerializer.Deserialize<List<ImeiCacheEntry>>(json, JsonOpts);
            if (list is null) return new List<ImeiCacheEntry>();
            return list.OrderByDescending(e => e.LastUsedUtc).ToList();
        }
        catch
        {
            return new List<ImeiCacheEntry>();
        }
    }

    private void Save()
    {
        try
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_entries, JsonOpts));
            if (File.Exists(_path)) File.Replace(tmp, _path, _path + ".bak", ignoreMetadataErrors: true);
            else File.Move(tmp, _path);
        }
        catch
        {
            // Disk full / ACL — non-fatal. Cache stays in-memory for this session.
        }
    }
}
