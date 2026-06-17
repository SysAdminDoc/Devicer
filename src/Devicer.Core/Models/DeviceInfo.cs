namespace Devicer.Core.Models;

public sealed record DeviceInfo
{
    public string Serial { get; init; } = "";
    public ConnectionState ConnectionState { get; init; } = ConnectionState.NotConnected;

    public string? Manufacturer { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public string? Codename { get; init; }
    public string? AndroidVersion { get; init; }
    public string? AndroidSdk { get; init; }
    public string? BuildFingerprint { get; init; }
    public string? BuildId { get; init; }
    public string? SecurityPatch { get; init; }

    public string? Csc { get; init; }
    public string? CscCountry { get; init; }
    /// <summary>
    /// Samsung PDA (AP firmware version, e.g. <c>S938BXXS6BYIF</c>). Extracted from build fingerprint
    /// or <c>ro.build.PDA</c>. Null on non-Samsung devices.
    /// </summary>
    public string? SamsungPda { get; init; }
    /// <summary>Samsung CSC (carrier) firmware version, e.g. <c>S938BOXM6BYIF</c>.</summary>
    public string? SamsungCscVersion { get; init; }
    public string? BasebandVersion { get; init; }
    public string? BootloaderVersion { get; init; }
    public string? CurrentSlot { get; init; }
    public bool? IsAbDevice { get; init; }
    public string? EncryptionState { get; init; }
    public bool? OemUnlockSupported { get; init; }
    public string? KnoxWarrantyBit { get; init; }

    public RootStatus Root { get; init; } = RootStatus.None;

    /// <summary>
    /// Device IMEI (14-15 digits). Probed via root <c>service call iphonesubinfo</c>.
    /// Used by the FUS BinaryInform request — Samsung's API rejects the legacy "0000…" fake
    /// IMEI as of late 2024.
    /// </summary>
    public string? Imei { get; init; }

    /// <summary>One UI version string (e.g. "8.0", "7.1"). Null on non-Samsung devices.</summary>
    public string? OneUiVersion { get; init; }

    /// <summary>
    /// True if the device has an init_boot partition (Android 13+ GKI 2.0).
    /// When true, root managers patch init_boot.img instead of boot.img.
    /// </summary>
    public bool HasInitBoot { get; init; }

    /// <summary>
    /// The correct partition name for root patching: "init_boot" on Android 13+ with GKI,
    /// "boot" on older devices.
    /// </summary>
    public string PatchTargetPartition => HasInitBoot ? "init_boot" : "boot";

    public bool IsSamsung =>
        (Manufacturer?.Contains("Samsung", StringComparison.OrdinalIgnoreCase) ?? false)
        || (Brand?.Contains("samsung", StringComparison.OrdinalIgnoreCase) ?? false);

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Model) && !string.IsNullOrWhiteSpace(Manufacturer))
                return $"{Manufacturer} {Model}";
            if (!string.IsNullOrWhiteSpace(Model))
                return Model!;
            return Serial;
        }
    }
}
