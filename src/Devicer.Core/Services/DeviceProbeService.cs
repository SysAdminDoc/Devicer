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

        bool? oemUnlock = props.TryGetValue("sys.oem_unlock_allowed", out var oem)
            ? oem == "1"
            : null;

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
            BuildFingerprint = Get(props, "ro.build.fingerprint"),
            BuildId = Get(props, "ro.build.id"),
            SecurityPatch = Get(props, "ro.build.version.security_patch"),
            Csc = Get(props, "ro.csc.sales_code") ?? Get(props, "ril.sales_code"),
            CscCountry = Get(props, "ro.csc.countryiso_code"),
            BasebandVersion = Get(props, "gsm.version.baseband") ?? Get(props, "ro.baseband"),
            BootloaderVersion = Get(props, "ro.bootloader") ?? Get(props, "ro.boot.bootloader"),
            CurrentSlot = Get(props, "ro.boot.slot_suffix"),
            IsAbDevice = props.TryGetValue("ro.build.ab_update", out var ab) ? ab == "true" : null,
            EncryptionState = Get(props, "ro.crypto.state"),
            OemUnlockSupported = oemUnlock,
            KnoxWarrantyBit = Get(props, "ro.boot.warranty_bit") ?? Get(props, "ro.warranty_bit"),
            Root = root,
        };
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
}
