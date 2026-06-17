namespace Devicer.Core.Models;

public enum PitBinaryType : int
{
    AP = 0,
    CP = 1,
}

public enum PitDeviceType : int
{
    OneNand = 0,
    File = 1,
    Mmc = 2,
    All = 3,
    Ufs = 8,
}

public enum PitPartitionType : int
{
    None = 0,
    Bct = 1,
    Bootloader = 2,
    Partition = 3,
    Gp1 = 4,
    Gp2 = 5,
}

public enum PitFilesystem : int
{
    None = 0,
    Basic = 1,
    Enhanced = 2,
    Ext4 = 3,
    Yaffs2 = 4,
    Lfs = 5,
}

public sealed record PitEntry
{
    public PitBinaryType BinaryType { get; init; }
    public PitDeviceType DeviceType { get; init; }
    public int Id { get; init; }
    public PitPartitionType PartitionType { get; init; }
    public PitFilesystem Filesystem { get; init; }
    public uint BlockOffset { get; init; }
    public uint BlockCount { get; init; }
    public uint FileOffset { get; init; }
    public uint FileSize { get; init; }
    public required string PartitionName { get; init; }
    public required string FlashFileName { get; init; }
    public required string FotaFileName { get; init; }
}

public sealed record PitFile
{
    public const uint Magic = 0x12349876;
    public int EntryCount { get; init; }
    public int LunCount { get; init; }
    public required IReadOnlyList<PitEntry> Entries { get; init; }
}
