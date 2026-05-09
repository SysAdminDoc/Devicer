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
    public string? BasebandVersion { get; init; }
    public string? BootloaderVersion { get; init; }
    public string? CurrentSlot { get; init; }
    public bool? IsAbDevice { get; init; }
    public string? EncryptionState { get; init; }
    public bool? OemUnlockSupported { get; init; }
    public string? KnoxWarrantyBit { get; init; }

    public RootStatus Root { get; init; } = RootStatus.None;

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
