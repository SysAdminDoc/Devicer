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
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Devicer");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        Settings = Load();
    }

    public string SettingsPath => _path;

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
            // Corrupt settings file shouldn't brick the app — start fresh, keep going.
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
            }
            catch
            {
                // Disk full, ACL block, etc. — fail silently; user notices stale settings, not a crash.
            }
        }
    }
}
