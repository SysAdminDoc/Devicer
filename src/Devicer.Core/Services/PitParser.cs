using System.IO;
using System.Text;
using Devicer.Core.Models;

namespace Devicer.Core.Services;

public interface IPitParser
{
    PitFile Parse(byte[] data);
    PitFile Parse(Stream stream);
    Task<PitFile> ParseFileAsync(string path, CancellationToken ct = default);
}

public sealed class PitParser : IPitParser
{
    private const int HeaderSize = 28;
    private const int EntrySize = 132;

    public async Task<PitFile> ParseFileAsync(string path, CancellationToken ct = default)
    {
        var data = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        return Parse(data);
    }

    public PitFile Parse(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Parse(ms.ToArray());
    }

    public PitFile Parse(byte[] data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException($"PIT data too short ({data.Length} bytes, minimum {HeaderSize}).");

        using var reader = new BinaryReader(new MemoryStream(data), Encoding.Unicode, leaveOpen: false);

        var magic = reader.ReadUInt32();
        if (magic != PitFile.Magic)
            throw new InvalidDataException($"Invalid PIT magic: 0x{magic:X8} (expected 0x{PitFile.Magic:X8}).");

        var entryCount = reader.ReadInt32();
        var dummy1 = reader.ReadInt32();
        var dummy2 = reader.ReadInt32();
        var dummy3 = reader.ReadInt32();
        var dummy4 = reader.ReadInt32();
        var lunCount = reader.ReadInt32();

        if (entryCount < 0 || entryCount > 1000)
            throw new InvalidDataException($"PIT entry count {entryCount} is out of range.");

        var expectedSize = HeaderSize + (entryCount * EntrySize);
        if (data.Length < expectedSize)
            throw new InvalidDataException($"PIT data truncated: {data.Length} bytes, expected {expectedSize} for {entryCount} entries.");

        var entries = new List<PitEntry>(entryCount);
        for (int i = 0; i < entryCount; i++)
        {
            var offset = HeaderSize + (i * EntrySize);
            entries.Add(ParseEntry(data, offset));
        }

        return new PitFile
        {
            EntryCount = entryCount,
            LunCount = lunCount,
            Entries = entries,
        };
    }

    private static PitEntry ParseEntry(byte[] data, int offset)
    {
        var binaryType = (PitBinaryType)BitConverter.ToInt32(data, offset);
        var deviceType = (PitDeviceType)BitConverter.ToInt32(data, offset + 4);
        var id = BitConverter.ToInt32(data, offset + 8);
        var partitionType = (PitPartitionType)BitConverter.ToInt32(data, offset + 12);
        var filesystem = (PitFilesystem)BitConverter.ToInt32(data, offset + 16);
        var blockOffset = BitConverter.ToUInt32(data, offset + 20);
        var blockCount = BitConverter.ToUInt32(data, offset + 24);
        var fileOffset = BitConverter.ToUInt32(data, offset + 28);
        var fileSize = BitConverter.ToUInt32(data, offset + 32);

        var partitionName = ReadNullTerminatedUtf16(data, offset + 36, 32);
        var flashFileName = ReadNullTerminatedUtf16(data, offset + 68, 32);
        var fotaFileName = ReadNullTerminatedUtf16(data, offset + 100, 32);

        return new PitEntry
        {
            BinaryType = binaryType,
            DeviceType = deviceType,
            Id = id,
            PartitionType = partitionType,
            Filesystem = filesystem,
            BlockOffset = blockOffset,
            BlockCount = blockCount,
            FileOffset = fileOffset,
            FileSize = fileSize,
            PartitionName = partitionName,
            FlashFileName = flashFileName,
            FotaFileName = fotaFileName,
        };
    }

    private static string ReadNullTerminatedUtf16(byte[] data, int offset, int maxBytes)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < maxBytes; i += 2)
        {
            if (offset + i + 1 >= data.Length) break;
            var ch = (char)(data[offset + i] | (data[offset + i + 1] << 8));
            if (ch == '\0') break;
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
