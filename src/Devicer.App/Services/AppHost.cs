using Devicer.Core.Services;

namespace Devicer.App.Services;

/// <summary>
/// Composition root. Manual DI to keep dependencies minimal — no Microsoft.Extensions.DependencyInjection in v0.2.x.
/// </summary>
public sealed class AppHost
{
    public IShellRunner ShellRunner { get; }
    public IAdbService Adb { get; }
    public IFastbootService Fastboot { get; }
    public IDeviceProbeService DeviceProbe { get; }
    public IFirmwareCheckService FirmwareCheck { get; }
    public AppSettingsStore SettingsStore { get; }
    public ThemeManager Theme { get; }

    public AppHost()
    {
        ShellRunner = new ShellRunner();
        Adb = new AdbService(ShellRunner);
        Fastboot = new FastbootService(ShellRunner);
        DeviceProbe = new DeviceProbeService(Adb, Fastboot);
        FirmwareCheck = new FirmwareCheckService();
        SettingsStore = new AppSettingsStore();
        Theme = new ThemeManager(SettingsStore);
    }
}
