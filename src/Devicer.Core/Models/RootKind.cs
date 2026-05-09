namespace Devicer.Core.Models;

public enum RootKind
{
    None,
    Magisk,
    KernelSU,
    APatch,
    Other,
}

public sealed record RootStatus(RootKind Kind, string? Version)
{
    public static RootStatus None { get; } = new(RootKind.None, null);

    public bool IsRooted => Kind != RootKind.None;
}
