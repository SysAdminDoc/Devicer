using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Devicer.Core.Services;

/// <summary>
/// One persistent IMEI entry. The model/CSC the IMEI was last paired with is captured
/// alongside so the dropdown can label entries (e.g. "354237929314284: SM-S938B/EUX").
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
                ? $" :  {Model}{(string.IsNullOrWhiteSpace(Csc) ? "" : $" / {Csc}")}"
                : string.Empty;
            return $"{Imei}{ctx}";
        }
    }
}

/// <summary>
/// DPAPI-encrypted IMEI cache at <c>%LOCALAPPDATA%\Devicer\imei-cache.json</c>. Saves every
/// IMEI a user commits to a download, so they don't have to retype 15 digits next time.
/// Entries are sorted newest-used-first; the cache is capped at <see cref="MaxEntries"/>
/// to keep the dropdown short. IMEI data is encrypted at rest using Windows DPAPI
/// (<see cref="DataProtectionScope.CurrentUser"/>) since IMEI is GDPR-classified personal data.
/// </summary>
public sealed class ImeiCache
{
    public const int MaxEntries = 20;

    private static readonly byte[] Entropy = "Devicer.ImeiCache.v1"u8.ToArray();

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
            var raw = File.ReadAllBytes(_path);
            if (raw.Length == 0) return new List<ImeiCacheEntry>();

            string json;
            if (raw[0] == (byte)'[' || raw[0] == (byte)'{')
            {
                json = Encoding.UTF8.GetString(raw);
            }
            else
            {
#pragma warning disable CA1416 // Devicer is a WPF app: Windows-only
                var decrypted = ProtectedData.Unprotect(raw, Entropy, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
                json = Encoding.UTF8.GetString(decrypted);
            }

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
            var json = JsonSerializer.Serialize(_entries, JsonOpts);
            var plainBytes = Encoding.UTF8.GetBytes(json);
#pragma warning disable CA1416 // Devicer is a WPF app: Windows-only
            var encrypted = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416

            var tmp = _path + ".tmp";
            File.WriteAllBytes(tmp, encrypted);
            if (File.Exists(_path)) File.Replace(tmp, _path, _path + ".bak", ignoreMetadataErrors: true);
            else File.Move(tmp, _path);
        }
        catch
        {
            // Disk full / ACL: non-fatal. Cache stays in-memory for this session.
        }
    }
}
