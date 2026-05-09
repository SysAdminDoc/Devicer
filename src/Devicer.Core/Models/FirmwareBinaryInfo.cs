namespace Devicer.Core.Models;

/// <summary>
/// Result of a FUS BinaryInform call. Describes the encrypted firmware blob the server
/// will hand over and the data needed to decrypt it.
/// </summary>
public sealed record FirmwareBinaryInfo
{
    /// <summary>Server-relative file name (e.g. <c>SW_S938BXXS6BYIF_...zip.enc4</c>).</summary>
    public required string BinaryName { get; init; }

    /// <summary>Total size of the encrypted blob in bytes.</summary>
    public required long BinaryByteSize { get; init; }

    /// <summary>Server-side download path component used to assemble the URL on the cloud host.</summary>
    public required string ModelPath { get; init; }

    /// <summary>The decryption-key payload (LATEST_FW_VERSION). Used in V4 key derivation.</summary>
    public required string LatestFwVersion { get; init; }

    /// <summary>The decryption-key payload (LOGIC_VALUE_FACTORY). Used in V4 key derivation.</summary>
    public required string LogicValueFactory { get; init; }

    /// <summary>True if the encrypted blob is V4 (.enc4); false for legacy V2 (.enc2).</summary>
    public bool IsV4 => BinaryName.EndsWith(".enc4", StringComparison.OrdinalIgnoreCase);
}
