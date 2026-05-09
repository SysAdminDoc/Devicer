using Devicer.Core.Services;

namespace Devicer.App.Services;

/// <summary>
/// Composition root. Manual DI to keep dependencies minimal — no Microsoft.Extensions.DependencyInjection in v0.2.0.
/// </summary>
public sealed class AppHost
{
    public IShellRunner ShellRunner { get; }
    public IAdbService Adb { get; }
    public IFastbootService Fastboot { get; }
    public IDeviceProbeService DeviceProbe { get; }

    public AppHost()
    {
        ShellRunner = new ShellRunner();
        Adb = new AdbService(ShellRunner);
        Fastboot = new FastbootService(ShellRunner);
        DeviceProbe = new DeviceProbeService(Adb, Fastboot);
    }
}
