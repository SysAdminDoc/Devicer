namespace Devicer.Core.Models;

/// <summary>
/// A Samsung firmware version string. The wire format is slash-separated:
///   PDA/CSC/CP/BOOT  (4 parts, modern)
///   PDA/CSC/CP        (3 parts, older)
///
/// Example: <c>S938BXXS6BYIF/S938BOXM6BYIF/S938BXXS6BYIF/S938BXXS6BYIF</c>.
/// </summary>
public sealed record FirmwareVersion(string Pda, string Csc, string Cp, string? Boot = null)
{
    public string Raw => Boot is null
        ? $"{Pda}/{Csc}/{Cp}"
        : $"{Pda}/{Csc}/{Cp}/{Boot}";

    public static FirmwareVersion? TryParse(string? slashSeparated)
    {
        if (string.IsNullOrWhiteSpace(slashSeparated)) return null;
        var parts = slashSeparated.Trim().Split('/', StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            3 => new FirmwareVersion(parts[0], parts[1], parts[2]),
            >= 4 => new FirmwareVersion(parts[0], parts[1], parts[2], parts[3]),
            _ => null,
        };
    }

    /// <summary>
    /// Compares two PDA strings (positions 7..) which encode a YYYYMM build code.
    /// Returns &gt;0 if <paramref name="a"/> is newer, &lt;0 if older, 0 if equal-or-uncomparable.
    /// </summary>
    public static int ComparePda(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return 0;
        // Compare lexicographically — Samsung PDAs sort correctly under string ordering for the same model.
        return string.Compare(a, b, StringComparison.Ordinal);
    }

    /// <summary>
    /// Normalizes a version string for FUS consumption:
    ///   • If 3 parts, append PDA as BOOT (Samsung's BinaryInform endpoint expects 4).
    ///   • If a middle part is empty, fill with PDA.
    /// </summary>
    public string Normalized
    {
        get
        {
            var pda = string.IsNullOrWhiteSpace(Pda) ? "" : Pda;
            var csc = string.IsNullOrWhiteSpace(Csc) ? pda : Csc;
            var cp = string.IsNullOrWhiteSpace(Cp) ? pda : Cp;
            var boot = string.IsNullOrWhiteSpace(Boot) ? pda : Boot;
            return $"{pda}/{csc}/{cp}/{boot}";
        }
    }
}
