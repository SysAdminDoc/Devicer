using Devicer.Core.Models;

namespace Devicer.Core.Services;

/// <summary>One actionable item in an OEM's flashing workflow.</summary>
public sealed record OemStep(string Title, string Detail, string? Url = null);

/// <summary>OEM-specific guidance: unlock procedure, recommended tool, portal link, quirks.</summary>
public sealed record OemGuidance
{
    public required OemKind Oem { get; init; }
    public required string Headline { get; init; }
    public required string Tooling { get; init; }
    public required IReadOnlyList<OemStep> UnlockSteps { get; init; }
    public required IReadOnlyList<OemStep> FlashSteps { get; init; }
    public required IReadOnlyList<OemStep> Quirks { get; init; }
    public string? PortalUrl { get; init; }
    public string? PortalLabel { get; init; }
}

public interface IOemGuidanceService
{
    OemGuidance For(OemKind oem);
}

public sealed class OemGuidanceService : IOemGuidanceService
{
    public OemGuidance For(OemKind oem) => oem switch
    {
        OemKind.Google => Pixel(),
        OemKind.OnePlus => OnePlus(),
        OemKind.Xiaomi => Xiaomi(),
        OemKind.Sony => Sony(),
        OemKind.Asus => Asus(),
        OemKind.Motorola => Motorola(),
        OemKind.Nothing => Nothing(),
        OemKind.Samsung => Samsung(),
        _ => Generic(oem),
    };

    private static OemGuidance Pixel() => new()
    {
        Oem = OemKind.Google,
        Headline = "Pixel uses fastboot end-to-end. Google publishes factory images and a web-based flasher.",
        Tooling = "fastboot (Platform-Tools) + Google's official Android Flash Tool (web-based).",
        UnlockSteps =
        [
            new("Enable OEM unlocking + USB debugging", "Settings → About → tap Build seven times → Developer options → toggle 'OEM unlocking' AND 'USB debugging'."),
            new("Reboot to bootloader", "From the device: hold Power + Volume Down. Or via adb: `adb reboot bootloader`."),
            new("fastboot flashing unlock", "On the host: `fastboot flashing unlock`. Confirm on device with Volume keys + Power. WIPES USERDATA."),
        ],
        FlashSteps =
        [
            new("Use Android Flash Tool (recommended)", "Browser-based flasher; pulls factory images straight from Google.", "https://flash.android.com/"),
            new("Or factory images + flash-all.sh", "Download the device's factory image ZIP, unzip, run `flash-all.bat` (Windows) / `flash-all.sh` from a fastboot shell.", "https://developers.google.com/android/images"),
        ],
        Quirks =
        [
            new("Anti-rollback (ARB)", "Pixel partitions enforce monotonically-increasing version. You CANNOT downgrade across an ARB boundary — the device will refuse to boot or brick. Always check the factory-image release notes for ARB warnings."),
            new("init_boot.img on Pixel 7+", "Pixel 7 and newer ship a separate init_boot partition. Magisk-patch init_boot, NOT boot, on those models."),
        ],
        PortalUrl = "https://flash.android.com/",
        PortalLabel = "Open Android Flash Tool",
    };

    private static OemGuidance OnePlus() => new()
    {
        Oem = OemKind.OnePlus,
        Headline = "OnePlus uses standard fastboot for flashing. MSM Tool (Windows-only, vendor-restricted) handles unbrick.",
        Tooling = "fastboot (Platform-Tools) for normal flashing; OnePlus MSM Download Tool for EDL recovery.",
        UnlockSteps =
        [
            new("Enable OEM unlocking + USB debugging", "Settings → About → tap Build seven times → Developer options → toggle 'OEM unlocking' AND 'USB debugging'."),
            new("Reboot to bootloader", "Hold Power + Volume Up (varies by model). Or `adb reboot bootloader`."),
            new("fastboot oem unlock", "Older OnePlus (5/5T era) use `fastboot oem unlock`; newer use `fastboot flashing unlock`. Try the second first."),
        ],
        FlashSteps =
        [
            new("flash boot/system via fastboot", "Standard `fastboot flash boot boot.img` etc. for individual partitions."),
            new("Full firmware via MSM Tool", "If the device is bricked / EDL-mode, OnePlus's MSM Download Tool re-flashes everything. Windows-only, requires the matching MSM package for the model.", "https://oxygenupdater.com/articles/the-ultimate-guide-to-oneplus-msm-flashing/"),
        ],
        Quirks =
        [
            new("Encrypted MSM packages", "MSM ROMs are scoped per-region and are NOT generally redistributable; don't use mirror downloads of unknown provenance."),
            new("OxygenOS vs ColorOS rollback", "Some recent OnePlus models share their bootloader with OPPO; cross-flashing OxygenOS ↔ ColorOS may permanently lock the device. Stay on the firmware family the device shipped with."),
        ],
        PortalUrl = "https://oxygenupdater.com/articles/the-ultimate-guide-to-oneplus-msm-flashing/",
        PortalLabel = "OnePlus MSM flashing guide",
    };

    private static OemGuidance Xiaomi() => new()
    {
        Oem = OemKind.Xiaomi,
        Headline = "Xiaomi (Mi / Redmi / POCO) requires the Mi Unlock Tool + a 7-day waiting period before unlock.",
        Tooling = "Mi Unlock Tool (Windows) + MiFlash (Windows fastboot/EDL flasher).",
        UnlockSteps =
        [
            new("Sign in to Mi Account on the phone", "Settings → Mi Account. The account must own the device for ≥3 days before unlock."),
            new("Add Mi Unlock binding", "Settings → Additional → Developer options → Mi Unlock status → Add account and device. Triggers the unlock waiting period."),
            new("Wait 168 hours (7 days)", "Xiaomi enforces this server-side. The Mi Unlock Tool will reject the unlock until the timer expires."),
            new("Run Mi Unlock Tool", "On the host, with the device in fastboot mode: launch Mi Unlock and click Unlock. WIPES USERDATA.", "https://en.miui.com/unlock/"),
        ],
        FlashSteps =
        [
            new("MiFlash for full firmware", "Extract the official fastboot ROM, point MiFlash at it, choose 'clean all' or 'save user data', click Flash.", "https://xiaomifirmwareupdater.com/miflash/"),
            new("Per-partition fastboot", "If you have individual .img files: `fastboot flash boot boot.img`, etc."),
        ],
        Quirks =
        [
            new("Anti-rollback (ARB)", "Xiaomi enforces ARB on most devices. Downgrading across ARB boundaries bricks the device. Always check the firmware's ARB index before flashing."),
            new("EU vs Global vs China ROM mixing", "Cross-region ROM flashing (Global → China, etc.) often requires a specific intermediate stable ROM. xiaomifirmwareupdater.com publishes the path."),
            new("MIUI / HyperOS bootloader changes 2024+", "Newer Xiaomi devices may revert to a locked state after factory reset; re-unlock the bootloader via the same Mi Unlock procedure."),
        ],
        PortalUrl = "https://en.miui.com/unlock/",
        PortalLabel = "Mi Unlock portal",
    };

    private static OemGuidance Sony() => new()
    {
        Oem = OemKind.Sony,
        Headline = "Sony Xperia uses fastboot + Sony's developer portal for unlock codes. Camera DRM keys are LOST on unlock.",
        Tooling = "fastboot + Newflasher (community tool) for full Sony firmware (.ftf) flashing.",
        UnlockSteps =
        [
            new("Get IMEI", "Dial *#06# or check Settings → About."),
            new("Request unlock code", "Sony's developer portal accepts the IMEI and emails an unlock code for compatible models.", "https://developer.sony.com/develop/open-devices/get-started/unlock-bootloader"),
            new("fastboot oem unlock", "`fastboot oem unlock 0x<your-code>`. WIPES USERDATA. Sony's camera DRM keys are also wiped — image quality degrades on most models."),
        ],
        FlashSteps =
        [
            new("Newflasher for Xperia .ftf packages", "Community tool; reads Sony's official .ftf firmware via the Sony EMMA-style protocol.", "https://forum.xda-developers.com/t/tool-newflasher-experimental-flash-tool-for-sony-xperia-devices.3619426/"),
        ],
        Quirks =
        [
            new("DRM-key loss", "Unlocking on Sony permanently destroys the camera DRM keys; HDR / NR algorithms may degrade. Backup the TA partition with Sony Flashtool BEFORE unlocking."),
        ],
        PortalUrl = "https://developer.sony.com/develop/open-devices/get-started/unlock-bootloader",
        PortalLabel = "Sony unlock portal",
    };

    private static OemGuidance Asus() => new()
    {
        Oem = OemKind.Asus,
        Headline = "ASUS publishes a per-device 'Unlock Tool' APK + raw fastboot for flashing.",
        Tooling = "ASUS Unlock Tool (APK on device) + fastboot.",
        UnlockSteps =
        [
            new("Download ASUS Unlock Tool", "ASUS publishes a model-specific APK on its support site. Side-load and run.", "https://www.asus.com/support/"),
            new("Reboot to bootloader and confirm", "After the APK confirmation, the device reboots, takes the unlock, and wipes data."),
        ],
        FlashSteps =
        [
            new("fastboot flash for individual partitions", "ASUS publishes raw images on the per-model support page."),
        ],
        Quirks =
        [
            new("Warranty-void marker", "Like other OEMs, unlocking voids ASUS warranty server-side."),
        ],
        PortalUrl = "https://www.asus.com/support/",
        PortalLabel = "ASUS support",
    };

    private static OemGuidance Motorola() => new()
    {
        Oem = OemKind.Motorola,
        Headline = "Motorola issues per-IMEI unlock codes via its developer portal. Lenovo (parent) shares the protocol.",
        Tooling = "fastboot + Motorola unlock portal for codes.",
        UnlockSteps =
        [
            new("Get unique key", "On the device in fastboot: `fastboot oem get_unlock_data` — concatenate the lines into one string."),
            new("Submit to Motorola portal", "The portal validates the key and emails an unlock code for eligible models.", "https://en-us.support.motorola.com/app/standalone/bootloader/unlock-your-device-a"),
            new("fastboot oem unlock <code>", "WIPES USERDATA. Some carrier-locked variants are NOT unlockable."),
        ],
        FlashSteps =
        [
            new("RSA / Lenovo Moto fastboot images", "Motorola publishes per-device 'Stock' firmware that flashes via a `flashfile.xml` script — community tools (mfastboot / Lenovo SAM Tool) execute it."),
        ],
        Quirks =
        [
            new("Carrier locks", "Verizon-branded Motos are typically NOT unlockable regardless of IMEI."),
        ],
        PortalUrl = "https://en-us.support.motorola.com/app/standalone/bootloader/unlock-your-device-a",
        PortalLabel = "Motorola unlock portal",
    };

    private static OemGuidance Nothing() => new()
    {
        Oem = OemKind.Nothing,
        Headline = "Nothing Phone (1/2/2a) uses fastboot and publishes factory images.",
        Tooling = "fastboot (Platform-Tools) + Nothing's factory firmware ZIPs.",
        UnlockSteps =
        [
            new("Enable OEM unlocking + USB debugging", "Standard developer-options dance."),
            new("fastboot flashing unlock", "From bootloader: `fastboot flashing unlock`. WIPES USERDATA."),
        ],
        FlashSteps =
        [
            new("Factory firmware ZIP", "Nothing publishes per-device firmware on its support site; extract and run `flash-all`.", "https://intl.nothing.tech/pages/support"),
        ],
        Quirks =
        [
            new("AVB + dm-verity", "Stock Nothing firmware enforces AVB. Magisk-patch the matching boot.img and disable verity if you need a custom kernel."),
        ],
        PortalUrl = "https://intl.nothing.tech/pages/support",
        PortalLabel = "Nothing support",
    };

    private static OemGuidance Samsung() => new()
    {
        Oem = OemKind.Samsung,
        Headline = "Samsung uses Odin protocol — Devicer's Firmware + Flash tabs are dedicated to Samsung. This page is for non-Samsung devices.",
        Tooling = "(Use the Firmware and Flash tabs.)",
        UnlockSteps = [],
        FlashSteps = [],
        Quirks =
        [
            new("OEM unlock toggle removed in One UI 8", "Samsung removed the 'OEM unlocking' developer toggle on Galaxy S25 / Z Fold7 / Z Flip7 with One UI 8 / Android 16. Bootloader unlock is currently NOT available on those models — flashing custom AP/CSC will fail with a SECURE-CHECK error."),
            new("Maintenance Mode required for Download Mode (One UI 8.5+)", "Starting with One UI 8.5, Samsung requires Maintenance Mode to be enabled before the Volume Down + USB key combo enters Download Mode. Without it, the device shows a blank blue screen. Enable Maintenance Mode in Settings before attempting to flash."),
        ],
        PortalUrl = null,
    };

    private static OemGuidance Generic(OemKind kind) => new()
    {
        Oem = kind,
        Headline = $"No OEM-specific profile for {kind.DisplayName()} yet — fall back to standard fastboot.",
        Tooling = "fastboot (Platform-Tools).",
        UnlockSteps =
        [
            new("Enable OEM unlocking + USB debugging", "Settings → About → tap Build seven times → Developer options → toggle 'OEM unlocking' AND 'USB debugging'."),
            new("Reboot to bootloader", "`adb reboot bootloader`."),
            new("fastboot flashing unlock", "Confirm on device. WIPES USERDATA."),
        ],
        FlashSteps =
        [
            new("Per-partition fastboot", "`fastboot flash <partition> <file.img>` — works for boot, vbmeta, system, vendor, dtbo, init_boot, etc."),
        ],
        Quirks =
        [
            new("Verify the manufacturer profile", "If your OEM has specific quirks (anti-rollback, DRM keys, unlock waiting period), check the official support site before flashing."),
        ],
        PortalUrl = null,
    };
}
