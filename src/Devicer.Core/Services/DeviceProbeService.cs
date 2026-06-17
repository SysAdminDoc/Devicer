using Devicer.Core.Models;

namespace Devicer.Core.Services;

public sealed record ProbeResult(IReadOnlyList<DeviceInfo> Devices, string? Diagnostic = null);

public interface IDeviceProbeService
{
    Task<ProbeResult> ProbeAsync(CancellationToken ct = default);
}

public sealed class DeviceProbeService : IDeviceProbeService
{
    private readonly IAdbService _adb;
    private readonly IFastbootService _fastboot;

    public DeviceProbeService(IAdbService adb, IFastbootService fastboot)
    {
        _adb = adb;
        _fastboot = fastboot;
    }

    public async Task<ProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        var adbAvailable = await _adb.IsAvailableAsync(ct).ConfigureAwait(false);
        var fastbootAvailable = await _fastboot.IsAvailableAsync(ct).ConfigureAwait(false);

        if (!adbAvailable && !fastbootAvailable)
            return new ProbeResult(Array.Empty<DeviceInfo>(),
                "Platform-tools not found on PATH. Install Android SDK Platform-Tools v37+ and add adb/fastboot to PATH.");

        var devices = new List<DeviceInfo>();

        if (adbAvailable)
        {
            var adbDevices = await _adb.ListDevicesAsync(ct).ConfigureAwait(false);
            foreach (var d in adbDevices)
            {
                if (d.State == ConnectionState.Adb || d.State == ConnectionState.Recovery)
                    devices.Add(await BuildAdbDeviceInfoAsync(d.Serial, d.State, ct).ConfigureAwait(false));
                else
                    devices.Add(new DeviceInfo { Serial = d.Serial, ConnectionState = d.State });
            }
        }

        if (fastbootAvailable)
        {
            var fbDevices = await _fastboot.ListDevicesAsync(ct).ConfigureAwait(false);
            foreach (var d in fbDevices)
                devices.Add(await BuildFastbootDeviceInfoAsync(d.Serial, ct).ConfigureAwait(false));
        }

        return new ProbeResult(devices);
    }

    private async Task<DeviceInfo> BuildAdbDeviceInfoAsync(string serial, ConnectionState state, CancellationToken ct)
    {
        var props = await _adb.GetAllPropsAsync(serial, ct).ConfigureAwait(false);
        var root = state == ConnectionState.Adb
            ? await _adb.DetectRootAsync(serial, ct).ConfigureAwait(false)
            : RootStatus.None;

        // IMEI requires root on modern Android. Skip if no root or non-Adb state — surface as null.
        string? imei = null;
        if (state == ConnectionState.Adb && root.Kind != RootKind.None)
            imei = await _adb.ReadImeiAsync(serial, ct).ConfigureAwait(false);

        bool? oemUnlock = props.TryGetValue("sys.oem_unlock_allowed", out var oem)
            ? oem == "1"
            : null;

        var fingerprint = Get(props, "ro.build.fingerprint");
        var (pda, cscVer) = ExtractSamsungPda(props, fingerprint);

        var oneUiVersion = Get(props, "ro.build.version.oneui") ?? Get(props, "ro.build.display.oneui");

        var hasInitBoot = await DetectInitBootAsync(serial, props, root, ct).ConfigureAwait(false);

        return new DeviceInfo
        {
            Serial = serial,
            ConnectionState = state,
            Manufacturer = Get(props, "ro.product.manufacturer"),
            Brand = Get(props, "ro.product.brand"),
            Model = Get(props, "ro.product.model"),
            Codename = Get(props, "ro.product.device") ?? Get(props, "ro.product.name"),
            AndroidVersion = Get(props, "ro.build.version.release"),
            AndroidSdk = Get(props, "ro.build.version.sdk"),
            BuildFingerprint = fingerprint,
            BuildId = Get(props, "ro.build.id"),
            SecurityPatch = Get(props, "ro.build.version.security_patch"),
            Csc = Get(props, "ro.csc.sales_code") ?? Get(props, "ril.sales_code"),
            CscCountry = Get(props, "ro.csc.countryiso_code"),
            SamsungPda = pda,
            SamsungCscVersion = cscVer,
            BasebandVersion = Get(props, "gsm.version.baseband") ?? Get(props, "ro.baseband"),
            BootloaderVersion = Get(props, "ro.bootloader") ?? Get(props, "ro.boot.bootloader"),
            CurrentSlot = Get(props, "ro.boot.slot_suffix"),
            IsAbDevice = props.TryGetValue("ro.build.ab_update", out var ab) ? ab == "true" : null,
            EncryptionState = Get(props, "ro.crypto.state"),
            OemUnlockSupported = oemUnlock,
            KnoxWarrantyBit = Get(props, "ro.boot.warranty_bit") ?? Get(props, "ro.warranty_bit"),
            Root = root,
            Imei = imei,
            OneUiVersion = oneUiVersion,
            HasInitBoot = hasInitBoot,
        };
    }

    private async Task<bool> DetectInitBootAsync(string serial, IReadOnlyDictionary<string, string> props, RootStatus root, CancellationToken ct)
    {
        if (int.TryParse(Get(props, "ro.build.version.sdk"), out var sdk) && sdk >= 33)
        {
            if (root.Kind != RootKind.None)
            {
                var r = await _adb.RunSuAsync(serial, "ls /dev/block/by-name/init_boot 2>/dev/null", TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                if (r.Success && r.Stdout.Contains("init_boot", StringComparison.Ordinal))
                    return true;
            }
            return true;
        }
        return false;
    }

    private async Task<DeviceInfo> BuildFastbootDeviceInfoAsync(string serial, CancellationToken ct)
    {
        var vars = await _fastboot.GetAllVarsAsync(serial, ct).ConfigureAwait(false);

        return new DeviceInfo
        {
            Serial = serial,
            ConnectionState = ConnectionState.Fastboot,
            Manufacturer = GetVar(vars, "manufacturer") ?? GetVar(vars, "product-vendor"),
            Brand = GetVar(vars, "product"),
            Model = GetVar(vars, "product"),
            Codename = GetVar(vars, "product"),
            BuildFingerprint = GetVar(vars, "version-bootloader"),
            BootloaderVersion = GetVar(vars, "version-bootloader"),
            BasebandVersion = GetVar(vars, "version-baseband"),
            CurrentSlot = GetVar(vars, "current-slot"),
            IsAbDevice = vars.ContainsKey("slot-count"),
            OemUnlockSupported = vars.TryGetValue("unlock-ability", out var ua) ? ua == "1"
                : vars.TryGetValue("unlocked", out var u) ? u == "yes"
                : null,
        };
    }

    private static string? Get(IReadOnlyDictionary<string, string> d, string key)
        => d.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    private static string? GetVar(IReadOnlyDictionary<string, string> d, string key)
        => d.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    /// <summary>
    /// Extract Samsung PDA (AP version) and CSC version from props or build fingerprint.
    /// Samsung's modern fingerprint format has the PDA_CSC pair as the 5th '/' segment, e.g.
    /// <c>samsung/pa3qxxx/pa3q:16/BP2A.250605.031.A3/S938BXXS6BYIF_OXM6BYIF:user/release-keys</c>
    /// — the segment <c>S938BXXS6BYIF_OXM6BYIF</c> splits on '_' into PDA + CSC. Older devices
    /// expose <c>ro.build.PDA</c> directly; we prefer that when present.
    /// </summary>
    internal static (string? Pda, string? CscVer) ExtractSamsungPda(IReadOnlyDictionary<string, string> props, string? fingerprint)
    {
        var directPda = Get(props, "ro.build.PDA");
        var directCsc = Get(props, "ro.build.PDA.SUB") ?? Get(props, "ro.csc.version");
        if (!string.IsNullOrWhiteSpace(directPda)) return (directPda, directCsc);

        if (string.IsNullOrWhiteSpace(fingerprint)) return (null, null);
        var slashes = fingerprint!.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (slashes.Length < 5) return (null, null);

        var seg = slashes[4];
        // Strip the ":user"/":userdebug" suffix that follows the version pair.
        var colon = seg.IndexOf(':');
        if (colon > 0) seg = seg[..colon];

        if (string.IsNullOrWhiteSpace(seg)) return (null, null);
        var underscore = seg.IndexOf('_');
        if (underscore <= 0) return (seg, null);
        return (seg[..underscore], seg[(underscore + 1)..]);
    }
}
