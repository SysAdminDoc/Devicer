using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Devicer.App.ViewModels;
using Devicer.Core.Models;
using Devicer.Core.Services;

namespace Devicer.App.Services;

internal static class MarketingCaptureMode
{
    private const string EnabledVariable = "DEVICER_MARKETING_CAPTURE";
    private const string ViewVariable = "DEVICER_MARKETING_VIEW";
    private const string OutputVariable = "DEVICER_MARKETING_OUTPUT";

    private static readonly IReadOnlyDictionary<string, (string NavLabel, string FileName)> Views =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["device"] = ("Device", "01-device.png"),
            ["firmware"] = ("Firmware", "02-firmware.png"),
            ["roms"] = ("ROMs", "03-roms.png"),
            ["backup"] = ("Backup", "04-backup.png"),
            ["flash"] = ("Flash", "05-flash-safety.png"),
            ["settings"] = ("Settings", "06-settings.png"),
        };

    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnabledVariable), "1", StringComparison.Ordinal);

    public static string DataDirectory
    {
        get
        {
            if (!IsEnabled)
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Devicer");
            var profile = Environment.GetEnvironmentVariable("DEVICER_MARKETING_PROFILE");
            if (string.IsNullOrWhiteSpace(profile) || !Path.IsPathFullyQualified(profile))
                throw new InvalidOperationException("Capture mode requires an absolute temporary profile directory.");
            return profile;
        }
    }

    public static void ApplyDemoData(
        DeviceViewModel device,
        FirmwareViewModel firmware,
        RomViewModel roms,
        BackupViewModel backup,
        FlashPageViewModel flash)
    {
        // Marketing captures must never probe attached hardware. Stop the hot-plug timer
        // before applying representative data so a physical phone cannot alter a frame.
        device.Dispose();

        var sampleDevice = new DeviceInfo
        {
            Serial = "DEMO-S938B",
            ConnectionState = ConnectionState.Adb,
            Manufacturer = "Samsung",
            Brand = "samsung",
            Model = "Galaxy S25 Ultra (SM-S938B)",
            Codename = "pa3q",
            AndroidVersion = "16",
            AndroidSdk = "36",
            BuildFingerprint = "samsung/pa3qxxx/pa3q:16/BP2A.250605.031.A3/S938BXXSCCZH1:user/release-keys",
            BuildId = "S938BXXSCCZH1",
            SecurityPatch = "2026-08-01",
            Csc = "EUX",
            CscCountry = "Europe",
            SamsungPda = "S938BXXSCCZH1",
            SamsungCscVersion = "S938BOXMCCZH1",
            BasebandVersion = "S938BXXSCCZH1",
            BootloaderVersion = "S938BXXSCCZH1",
            CurrentSlot = "a",
            IsAbDevice = true,
            EncryptionState = "file-based",
            OemUnlockSupported = false,
            KnoxWarrantyBit = "0",
            Root = new RootStatus(RootKind.Magisk, "30.7"),
            OneUiVersion = "8.0",
            HasInitBoot = true,
        };

        device.Devices.Clear();
        device.Devices.Add(sampleDevice);
        device.SelectedDevice = sampleDevice;
        device.StatusText = "1 sample device connected";
        device.Diagnostic = null;
        device.IsProbing = false;

        firmware.PrefillFrom(sampleDevice);
        firmware.Model = "SM-S938B";
        firmware.Csc = "EUX, INS";
        firmware.CurrentBuildId = "S938BXXSBCZG2";
        var latest = new FirmwareVersion(
            "S938BXXSCCZH1",
            "S938BOXMCCZH1",
            "S938BXXSCCZH1",
            "S938BXXSCCZH1");
        var previous = new FirmwareVersion(
            "S938BXXSBCZG2",
            "S938BOXMBCZG2",
            "S938BXXSBCZG2",
            "S938BXXSBCZG2");
        firmware.RegionResults.Clear();
        var eux = new FirmwareRegionResultItem(
            new RegionalFirmwareResult("EUX", new LatestFirmware(latest, [latest, previous])),
            firmware.CurrentBuildId);
        var ins = new FirmwareRegionResultItem(
            new RegionalFirmwareResult("INS", new LatestFirmware(latest, [latest, previous])),
            firmware.CurrentBuildId);
        firmware.RegionResults.Add(eux);
        firmware.RegionResults.Add(ins);
        firmware.SelectedRegionResult = eux;

        roms.Codename = "husky";
        roms.StatusText = "2 sample LineageOS builds for husky.";
        roms.Diagnostic = null;
        roms.Results.Clear();
        roms.Results.Add(new RomEntry
        {
            Source = RomSource.LineageOS,
            Kind = RomKind.Nightly,
            Codename = "husky",
            Version = "23.2",
            BuildDate = new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero),
            SizeBytes = 1_387_892_587,
            FileName = "lineage-23.2-20260904-nightly-husky-signed.zip",
            DownloadUrl = new Uri("https://mirrorbits.lineageos.org/full/husky/20260904/lineage-23.2-20260904-nightly-husky-signed.zip"),
            Sha256 = "4e7e14af88ccee4d200cf2a49c7c9bca55c071941f693759325bf0d2d9c96e49",
            Maintainer = "LineageOS",
        });
        roms.Results.Add(new RomEntry
        {
            Source = RomSource.LineageOS,
            Kind = RomKind.Nightly,
            Codename = "husky",
            Version = "23.2",
            BuildDate = new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero),
            SizeBytes = 1_389_207_470,
            FileName = "lineage-23.2-20260828-nightly-husky-signed.zip",
            DownloadUrl = new Uri("https://mirrorbits.lineageos.org/full/husky/20260828/lineage-23.2-20260828-nightly-husky-signed.zip"),
            Sha256 = "4b2e641db5bbe7f6fbeed0afbf33fc247c0acd704367f5db28ce2bc6afb8e663",
            Maintainer = "LineageOS",
        });

        backup.PrefillFrom(sampleDevice);
        backup.Partitions.Clear();
        AddPartition(backup, "efs", "/dev/block/sda3", 64L * 1024 * 1024, true);
        AddPartition(backup, "modemst1", "/dev/block/sda11", 8L * 1024 * 1024, true);
        AddPartition(backup, "modemst2", "/dev/block/sda12", 8L * 1024 * 1024, true);
        AddPartition(backup, "persist", "/dev/block/sda18", 32L * 1024 * 1024, true);
        AddPartition(backup, "boot", "/dev/block/sdc7", 96L * 1024 * 1024, false);
        AddPartition(backup, "init_boot", "/dev/block/sdc8", 8L * 1024 * 1024, false);
        AddPartition(backup, "vbmeta", "/dev/block/sdc9", 64L * 1024, false);
        backup.StatusText = "Loaded 7 partitions. Four critical partitions are pre-selected.";
        backup.Diagnostic = null;

        flash.PrefillFrom(sampleDevice);
        var odinEntries = new[]
        {
            new OdinTarEntry { Name = "boot.img.lz4", SizeBytes = 100_663_296 },
            new OdinTarEntry { Name = "init_boot.img.lz4", SizeBytes = 8_388_608 },
            new OdinTarEntry { Name = "vbmeta.img.lz4", SizeBytes = 65_536 },
        };
        flash.Odin.ArchivePath = @"C:\Firmware\AP_S938BXXSCCZH1.tar.md5";
        flash.Odin.Info = new OdinTarInfo
        {
            Path = flash.Odin.ArchivePath,
            FileName = "AP_S938BXXSCCZH1.tar.md5",
            FileSize = 8_214_233_088,
            Entries = odinEntries,
            HasMd5Suffix = true,
        };
        flash.Odin.Entries.Clear();
        foreach (var entry in odinEntries)
            flash.Odin.Entries.Add(new TarEntryRow { Entry = entry, Selected = true });
        flash.Odin.StatusText = "Archive inspected. Dry run is ready; no data has been written.";
    }

    public static async Task CaptureAsync(Window window, MainViewModel viewModel)
    {
        var viewName = Environment.GetEnvironmentVariable(ViewVariable) ?? "device";
        var outputDirectory = Environment.GetEnvironmentVariable(OutputVariable);
        if (!Views.TryGetValue(viewName, out var view))
            throw new InvalidOperationException($"Unknown marketing view '{viewName}'.");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException($"{OutputVariable} is required in marketing capture mode.");

        var target = viewModel.NavItems.First(item =>
            string.Equals(item.Label, view.NavLabel, StringComparison.OrdinalIgnoreCase));
        viewModel.SelectedNavItem = target;

        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        await Task.Delay(350);
        window.UpdateLayout();

        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY),
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        bitmap.Render(window);

        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, view.FileName);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using (var stream = File.Create(outputPath))
            encoder.Save(stream);

        Application.Current.Shutdown(0);
    }

    private static void AddPartition(
        BackupViewModel backup,
        string name,
        string blockPath,
        long sizeBytes,
        bool critical)
    {
        backup.Partitions.Add(new PartitionRow
        {
            Selected = critical,
            Info = new PartitionInfo
            {
                Name = name,
                BlockPath = blockPath,
                SizeBytes = sizeBytes,
                IsCritical = critical,
                CriticalReason = critical ? PartitionInfo.ReasonFor(name) : null,
            },
        });
    }
}
