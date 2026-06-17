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
    public Func<IFirmwareDownloadService> FirmwareDownloadFactory { get; }
    public IRomAggregatorService RomAggregator { get; }
    public IRomDownloadService RomDownload { get; }
    public IBackupService Backup { get; }
    public IRestoreService Restore { get; }
    public ITetherbackService Tetherback { get; }
    public INeoBackupService NeoBackup { get; }
    public IBootPatchService BootPatch { get; }
    public IPcPatchService PcPatch { get; }
    public IOdinInspectorService OdinInspector { get; }
    public IFastbootFlashService FastbootFlash { get; }
    public IToolManager ToolManager { get; }
    public IThorService Thor { get; }
    public IHeimdallService Heimdall { get; }
    public IOemGuidanceService OemGuidance { get; }
    public IOemPluginRegistry PluginRegistry { get; }
    public ImeiCache ImeiCache { get; }
    public AppSettingsStore SettingsStore { get; }
    public ThemeManager Theme { get; }

    public AppHost()
    {
        ShellRunner = new ShellRunner();
        Adb = new AdbService(ShellRunner);
        Fastboot = new FastbootService(ShellRunner);
        DeviceProbe = new DeviceProbeService(Adb, Fastboot);
        FirmwareCheck = new FirmwareCheckService();
        // Each download gets its own client; FUS sessions can rotate state mid-flight,
        // and per-download isolation lets the user run sequential downloads cleanly.
        FirmwareDownloadFactory = () => new FirmwareDownloadService();
        RomAggregator = new RomAggregatorService();
        RomDownload = new RomDownloadService();
        Backup = new BackupService(Adb);
        Restore = new RestoreService(Adb);
        ToolManager = new ToolManager();
        Tetherback = new TetherbackService(ShellRunner, ToolManager);
        NeoBackup = new NeoBackupService(Adb);
        BootPatch = new BootPatchService(Adb);
        PcPatch = new PcPatchService(ShellRunner, ToolManager);
        OdinInspector = new OdinInspectorService();
        FastbootFlash = new FastbootFlashService(Fastboot);
        Thor = new ThorService(ShellRunner, ToolManager);
        Heimdall = new HeimdallService(ShellRunner, ToolManager);
        OemGuidance = new OemGuidanceService();
        PluginRegistry = new OemPluginRegistry();
        ImeiCache = new ImeiCache();
        SettingsStore = new AppSettingsStore();
        Theme = new ThemeManager(SettingsStore);
    }
}
