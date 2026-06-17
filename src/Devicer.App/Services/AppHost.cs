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
    public IPitParser PitParser { get; }
    public IPayloadExtractService PayloadExtract { get; }
    public IFastbootFlashService FastbootFlash { get; }
    public IToolManager ToolManager { get; }
    public IThorService Thor { get; }
    public IHeimdallService Heimdall { get; }
    public IOemGuidanceService OemGuidance { get; }
    public IHashService Hash { get; }
    public IOemPluginRegistry PluginRegistry { get; }
    public ImeiCache ImeiCache { get; }
    public SnackbarService Snackbar { get; }
    public AppSettingsStore SettingsStore { get; }
    public ThemeManager Theme { get; }

    public AppHost()
    {
        ShellRunner = new ShellRunner();
        Adb = new AdbService(ShellRunner);
        Fastboot = new FastbootService(ShellRunner);
        DeviceProbe = new DeviceProbeService(Adb, Fastboot);
        Hash = new HashService();
        FirmwareCheck = new FirmwareCheckService();
        // Each download gets its own client; FUS sessions can rotate state mid-flight,
        // and per-download isolation lets the user run sequential downloads cleanly.
        FirmwareDownloadFactory = () => new FirmwareDownloadService(Hash);
        RomAggregator = new RomAggregatorService();
        RomDownload = new RomDownloadService();
        Backup = new BackupService(Adb, Hash);
        Restore = new RestoreService(Adb, Hash);
        ToolManager = new ToolManager();
        Tetherback = new TetherbackService(ShellRunner, ToolManager);
        NeoBackup = new NeoBackupService(Adb);
        BootPatch = new BootPatchService(Adb, Hash);
        PcPatch = new PcPatchService(ShellRunner, ToolManager, Hash);
        OdinInspector = new OdinInspectorService();
        PitParser = new PitParser();
        PayloadExtract = new PayloadExtractService();
        FastbootFlash = new FastbootFlashService(Fastboot);
        Thor = new ThorService(ShellRunner, ToolManager);
        Heimdall = new HeimdallService(ShellRunner, ToolManager);
        OemGuidance = new OemGuidanceService();
        PluginRegistry = new OemPluginRegistry();
        Snackbar = new SnackbarService();
        ImeiCache = new ImeiCache();
        SettingsStore = new AppSettingsStore();
        Theme = new ThemeManager(SettingsStore);
    }
}
