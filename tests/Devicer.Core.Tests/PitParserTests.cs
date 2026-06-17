using Devicer.Core.Models;
using Devicer.Core.Services;

namespace Devicer.Core.Tests;

public class PitParserTests
{
    private static byte[] BuildMinimalPit(int entryCount = 0, uint magic = PitFile.Magic)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write(magic);
        w.Write(entryCount);
        w.Write(0); // dummy1
        w.Write(0); // dummy2
        w.Write(0); // dummy3
        w.Write(0); // dummy4
        w.Write(1); // lunCount

        for (int i = 0; i < entryCount; i++)
        {
            w.Write(0); // binaryType = AP
            w.Write(8); // deviceType = UFS
            w.Write(i); // id
            w.Write(3); // partitionType = Partition
            w.Write(3); // filesystem = Ext4
            w.Write((uint)0); // blockOffset
            w.Write((uint)1024); // blockCount
            w.Write((uint)0); // fileOffset
            w.Write((uint)0); // fileSize

            var nameBytes = new byte[32];
            var name = $"part{i}";
            for (int j = 0; j < name.Length && j * 2 + 1 < 32; j++)
            {
                nameBytes[j * 2] = (byte)name[j];
                nameBytes[j * 2 + 1] = 0;
            }
            w.Write(nameBytes); // partitionName (UTF-16LE, 32 bytes)

            var flashBytes = new byte[32];
            var flash = $"part{i}.img";
            for (int j = 0; j < flash.Length && j * 2 + 1 < 32; j++)
            {
                flashBytes[j * 2] = (byte)flash[j];
                flashBytes[j * 2 + 1] = 0;
            }
            w.Write(flashBytes); // flashFileName (UTF-16LE, 32 bytes)
            w.Write(new byte[32]); // fotaFileName (empty)
        }

        return ms.ToArray();
    }

    [Fact]
    public void Parse_valid_pit_with_entries()
    {
        var data = BuildMinimalPit(3);
        var parser = new PitParser();
        var pit = parser.Parse(data);

        Assert.Equal(3, pit.EntryCount);
        Assert.Equal(1, pit.LunCount);
        Assert.Equal(3, pit.Entries.Count);
        Assert.Equal("part0", pit.Entries[0].PartitionName);
        Assert.Equal("part1", pit.Entries[1].PartitionName);
        Assert.Equal("part2", pit.Entries[2].PartitionName);
        Assert.Equal("part0.img", pit.Entries[0].FlashFileName);
        Assert.Equal(PitDeviceType.Ufs, pit.Entries[0].DeviceType);
        Assert.Equal(PitPartitionType.Partition, pit.Entries[0].PartitionType);
        Assert.Equal(PitFilesystem.Ext4, pit.Entries[0].Filesystem);
    }

    [Fact]
    public void Parse_empty_pit()
    {
        var data = BuildMinimalPit(0);
        var parser = new PitParser();
        var pit = parser.Parse(data);
        Assert.Equal(0, pit.EntryCount);
        Assert.Empty(pit.Entries);
    }

    [Fact]
    public void Parse_rejects_wrong_magic()
    {
        var data = BuildMinimalPit(0, magic: 0xDEADBEEF);
        var parser = new PitParser();
        Assert.Throws<InvalidDataException>(() => parser.Parse(data));
    }

    [Fact]
    public void Parse_rejects_truncated_data()
    {
        var data = new byte[10];
        var parser = new PitParser();
        Assert.Throws<InvalidDataException>(() => parser.Parse(data));
    }

    [Fact]
    public void Parse_rejects_truncated_entries()
    {
        var data = BuildMinimalPit(5);
        var truncated = data[..100];
        var parser = new PitParser();
        Assert.Throws<InvalidDataException>(() => parser.Parse(truncated));
    }
}
