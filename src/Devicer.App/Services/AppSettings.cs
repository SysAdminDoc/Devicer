using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Devicer.App.Services;

public enum AppTheme
{
    Mocha,
    Latte,
}

public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Mocha;
    public bool FirstRunCompleted { get; set; } = false;
    public int ProbeIntervalSeconds { get; set; } = 4;
    public string? LastDeviceSerial { get; set; }
}

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _lock = new();

    public AppSettings Settings { get; private set; }

    public AppSettingsStore()
    {
        var dir = MarketingCaptureMode.DataDirectory;
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        Settings = Load();
    }

    public string SettingsPath => _path;

    /// <summary>
    /// Last write error message, if the most recent <see cref="Save"/> couldn't reach disk
    /// (full disk, ACL block, missing parent on roaming profiles, etc). Cleared on the next
    /// successful save. Useful for surfacing a Diagnostic banner instead of swallowing.
    /// </summary>
    public string? LastSaveError { get; private set; }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
        }
        catch
        {
            // Corrupt settings file shouldn't brick the app — start fresh, keep going. The
            // next save replaces the broken file (and the .bak preserves the old one for
            // post-mortem if File.Replace was the path taken).
            return new AppSettings();
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Settings, JsonOpts));
                if (File.Exists(_path)) File.Replace(tmp, _path, _path + ".bak", ignoreMetadataErrors: true);
                else File.Move(tmp, _path);
                LastSaveError = null;
            }
            catch (Exception ex)
            {
                // Disk full, ACL block, etc. — keep the in-memory state and surface the
                // error so the UI can warn rather than silently lose user preferences.
                LastSaveError = ex.Message;
            }
        }
    }
}
