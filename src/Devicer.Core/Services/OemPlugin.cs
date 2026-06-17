using Devicer.Core.Models;

namespace Devicer.Core.Services;

public sealed record OemPluginInfo(
    string Name,
    string Version,
    IReadOnlyList<OemKind> SupportedOems
);

public interface IOemPlugin
{
    OemPluginInfo Info { get; }
    OemGuidance GetGuidance(OemKind oem, DeviceInfo? device);
    Task<bool> CanFlashAsync(DeviceInfo device, CancellationToken ct = default);
    Task<string> FlashAsync(DeviceInfo device, string imagePath, IProgress<FastbootFlashProgress>? progress, CancellationToken ct = default);
}

public interface IOemPluginRegistry
{
    void Register(IOemPlugin plugin);
    IReadOnlyList<IOemPlugin> GetPluginsFor(OemKind oem);
    IReadOnlyList<IOemPlugin> All { get; }
}

public sealed class OemPluginRegistry : IOemPluginRegistry
{
    private readonly List<IOemPlugin> _plugins = new();
    private readonly Dictionary<OemKind, List<IOemPlugin>> _index = new();

    public IReadOnlyList<IOemPlugin> All => _plugins;

    public void Register(IOemPlugin plugin)
    {
        _plugins.Add(plugin);
        foreach (var oem in plugin.Info.SupportedOems)
        {
            if (!_index.TryGetValue(oem, out var list))
            {
                list = new List<IOemPlugin>();
                _index[oem] = list;
            }
            list.Add(plugin);
        }
        DevicerLog.Info("PluginRegistry", $"Registered plugin: {plugin.Info.Name} v{plugin.Info.Version} ({string.Join(", ", plugin.Info.SupportedOems)})");
    }

    public IReadOnlyList<IOemPlugin> GetPluginsFor(OemKind oem)
    {
        return _index.TryGetValue(oem, out var list) ? list : [];
    }
}
