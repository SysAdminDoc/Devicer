using System.IO;

namespace Devicer.Core.Services;

public sealed record ToolInfo(string Name, string? Path, bool IsAvailable, string? Version);

public interface IToolManager
{
    ToolInfo Locate(string toolName);
    void RegisterPath(string toolName, string path);
}

public sealed class ToolManager : IToolManager
{
    private readonly string _toolsRoot;
    private readonly Dictionary<string, string> _overrides = new(StringComparer.OrdinalIgnoreCase);

    public ToolManager(string? toolsRoot = null)
    {
        _toolsRoot = toolsRoot ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Devicer", "tools");
    }

    public void RegisterPath(string toolName, string path) => _overrides[toolName] = path;

    public ToolInfo Locate(string toolName)
    {
        if (_overrides.TryGetValue(toolName, out var overridePath) && File.Exists(overridePath))
            return new ToolInfo(toolName, overridePath, true, null);

        var cached = FindInCache(toolName);
        if (cached is not null)
            return new ToolInfo(toolName, cached, true, null);

        var onPath = FindOnPath(toolName);
        if (onPath is not null)
            return new ToolInfo(toolName, onPath, true, null);

        return new ToolInfo(toolName, null, false, null);
    }

    private string? FindInCache(string toolName)
    {
        var toolDir = System.IO.Path.Combine(_toolsRoot, toolName);
        if (!Directory.Exists(toolDir)) return null;

        foreach (var versionDir in Directory.EnumerateDirectories(toolDir).OrderDescending())
        {
            var exePath = System.IO.Path.Combine(versionDir, toolName + ".exe");
            if (File.Exists(exePath)) return exePath;
            var altPath = System.IO.Path.Combine(versionDir, "TheAirBlow.Thor.Shell.exe");
            if (File.Exists(altPath)) return altPath;
        }
        return null;
    }

    private static string? FindOnPath(string toolName)
    {
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(System.IO.Path.PathSeparator) ?? [];
        foreach (var dir in pathDirs)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var full = System.IO.Path.Combine(dir, toolName + ".exe");
            if (File.Exists(full)) return full;
        }
        return null;
    }
}
