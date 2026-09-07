using System.IO;

namespace Devicer.Core.Services;

public enum PayloadExtractPhase
{
    Parsing,
    Extracting,
    Done,
    Failed,
}

public sealed record PayloadExtractProgress(
    PayloadExtractPhase Phase,
    long BytesWritten,
    long? TotalBytes,
    string? Message = null
);

public sealed record PayloadPartitionInfo(string Name, long DataOffset, long DataLength, bool IsCompressed);

public interface IPayloadExtractService
{
    Task<IReadOnlyList<PayloadPartitionInfo>> ListPartitionsAsync(string payloadPath, CancellationToken ct = default);

    Task<string> ExtractPartitionAsync(
        string payloadPath,
        string partitionName,
        string outputDir,
        IProgress<PayloadExtractProgress>? progress,
        CancellationToken ct = default);
}

public sealed class PayloadExtractService : IPayloadExtractService
{
    public async Task<IReadOnlyList<PayloadPartitionInfo>> ListPartitionsAsync(string payloadPath, CancellationToken ct = default)
    {
        await using var fs = new FileStream(payloadPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        var (dataOffset, manifest) = await ReadManifestAsync(fs, ct).ConfigureAwait(false);
        return manifest.Select(p => new PayloadPartitionInfo(p.Name, dataOffset + p.DataOffset, p.DataLength, p.Operations.Any(o => o.Type != 0))).ToList();
    }

    public async Task<string> ExtractPartitionAsync(
        string payloadPath,
        string partitionName,
        string outputDir,
        IProgress<PayloadExtractProgress>? progress,
        CancellationToken ct = default)
    {
        DevicerLog.Section($"Payload extract: {partitionName} from {Path.GetFileName(payloadPath)}");
        progress?.Report(new PayloadExtractProgress(PayloadExtractPhase.Parsing, 0, null, "Parsing payload header…"));

        await using var fs = new FileStream(payloadPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        var (dataOffset, manifest) = await ReadManifestAsync(fs, ct).ConfigureAwait(false);

        var partition = manifest.FirstOrDefault(p => string.Equals(p.Name, partitionName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Partition '{partitionName}' not found in payload. Available: {string.Join(", ", manifest.Select(p => p.Name))}");

        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, $"{partitionName}.img");

        progress?.Report(new PayloadExtractProgress(PayloadExtractPhase.Extracting, 0, partition.TotalOutputSize,
            $"Extracting {partitionName} ({partition.Operations.Count} operations)…"));

        await using var outFs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

        long written = 0;
        var lastReport = Environment.TickCount64;
        foreach (var op in partition.Operations)
        {
            ct.ThrowIfCancellationRequested();
            fs.Position = dataOffset + op.DataOffset;
            var buf = new byte[op.DataLength];
            await ReadFullAsync(fs, buf, (int)op.DataLength, ct).ConfigureAwait(false);

            // Operation types: 0=REPLACE, 6=REPLACE_XZ, 8=REPLACE_BZ
            // For now we handle REPLACE (uncompressed) directly. Compressed
            // operations pass through raw: a future version can add XZ/BZ2 decompression.
            byte[] output = buf;

            if (op.DstOffset >= 0)
                outFs.Position = op.DstOffset;
            await outFs.WriteAsync(output, ct).ConfigureAwait(false);
            written += output.Length;

            var now = Environment.TickCount64;
            if (now - lastReport > 100)
            {
                progress?.Report(new PayloadExtractProgress(PayloadExtractPhase.Extracting, written, partition.TotalOutputSize));
                lastReport = now;
            }
        }

        progress?.Report(new PayloadExtractProgress(PayloadExtractPhase.Done, written, written, $"Extracted {partitionName}.img ({written:N0} bytes)"));
        return outputPath;
    }

    private static async Task<(long DataOffset, List<ManifestPartition> Partitions)> ReadManifestAsync(Stream fs, CancellationToken ct)
    {
        var header = new byte[24];
        await ReadFullAsync(fs, header, 24, ct).ConfigureAwait(false);

        if (header[0] != (byte)'C' || header[1] != (byte)'r' || header[2] != (byte)'A' || header[3] != (byte)'U')
            throw new InvalidDataException("Not a valid Android OTA payload (missing CrAU magic).");

        var version = ReadUint64BE(header, 4);
        var manifestSize = ReadUint64BE(header, 12);
        var metadataSigSize = version >= 2 ? ReadUint32BE(header, 20) : 0u;

        if (manifestSize > 100_000_000)
            throw new InvalidDataException($"Manifest size {manifestSize} too large: corrupt or unsupported payload.");

        var manifestBytes = new byte[manifestSize];
        await ReadFullAsync(fs, manifestBytes, (int)manifestSize, ct).ConfigureAwait(false);

        if (metadataSigSize > 0)
        {
            fs.Position += metadataSigSize;
        }

        var dataOffset = 24 + (long)manifestSize + metadataSigSize;

        var partitions = ParseManifestProtobuf(manifestBytes);
        return (dataOffset, partitions);
    }

    private static List<ManifestPartition> ParseManifestProtobuf(byte[] data)
    {
        var partitions = new List<ManifestPartition>();
        int pos = 0;
        while (pos < data.Length)
        {
            var (fieldNum, wireType) = ReadTag(data, ref pos);
            if (fieldNum == 13 && wireType == 2) // field 13 = partitions (repeated PartitionUpdate)
            {
                var len = ReadVarint(data, ref pos);
                var end = pos + (int)len;
                partitions.Add(ParsePartitionUpdate(data, pos, end));
                pos = end;
            }
            else
            {
                SkipField(data, wireType, ref pos);
            }
        }
        return partitions;
    }

    private static ManifestPartition ParsePartitionUpdate(byte[] data, int start, int end)
    {
        string name = "";
        var operations = new List<InstallOperation>();
        int pos = start;
        while (pos < end)
        {
            var (fieldNum, wireType) = ReadTag(data, ref pos);
            if (fieldNum == 1 && wireType == 2) // partition_info submessage
            {
                var len = ReadVarint(data, ref pos);
                var subEnd = pos + (int)len;
                int subPos = pos;
                while (subPos < subEnd)
                {
                    var (sf, sw) = ReadTag(data, ref subPos);
                    if (sf == 1 && sw == 2) // name
                        name = ReadString(data, ref subPos);
                    else
                        SkipField(data, sw, ref subPos);
                }
                pos = subEnd;
            }
            else if (fieldNum == 2 && wireType == 2) // operations (repeated InstallOperation)
            {
                var len = ReadVarint(data, ref pos);
                var subEnd = pos + (int)len;
                operations.Add(ParseInstallOperation(data, pos, subEnd));
                pos = subEnd;
            }
            else
            {
                SkipField(data, wireType, ref pos);
            }
        }
        return new ManifestPartition(name, operations);
    }

    private static InstallOperation ParseInstallOperation(byte[] data, int start, int end)
    {
        int type = 0;
        long dataOffset = 0, dataLength = 0, dstOffset = -1;
        int pos = start;
        while (pos < end)
        {
            var (fieldNum, wireType) = ReadTag(data, ref pos);
            switch (fieldNum)
            {
                case 1 when wireType == 0: type = (int)ReadVarint(data, ref pos); break;        // type enum
                case 4 when wireType == 0: dataOffset = (long)ReadVarint(data, ref pos); break;  // data_offset
                case 5 when wireType == 0: dataLength = (long)ReadVarint(data, ref pos); break;  // data_length
                case 6 when wireType == 2: // dst_extents (repeated Extent)
                    var len = ReadVarint(data, ref pos);
                    var extEnd = pos + (int)len;
                    int ep = pos;
                    while (ep < extEnd)
                    {
                        var (ef, ew) = ReadTag(data, ref ep);
                        if (ef == 1 && ew == 0) // start_block
                        {
                            var block = (long)ReadVarint(data, ref ep);
                            if (dstOffset < 0) dstOffset = block * 4096; // 4K block size
                        }
                        else SkipField(data, ew, ref ep);
                    }
                    pos = extEnd;
                    break;
                default: SkipField(data, wireType, ref pos); break;
            }
        }
        return new InstallOperation(type, dataOffset, dataLength, dstOffset);
    }

    // Minimal protobuf varint/tag/skip helpers
    private static ulong ReadVarint(byte[] data, ref int pos)
    {
        ulong result = 0; int shift = 0;
        while (pos < data.Length)
        {
            byte b = data[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
        }
        return result;
    }

    private static (int FieldNum, int WireType) ReadTag(byte[] data, ref int pos)
    {
        var tag = ReadVarint(data, ref pos);
        return ((int)(tag >> 3), (int)(tag & 7));
    }

    private static string ReadString(byte[] data, ref int pos)
    {
        var len = (int)ReadVarint(data, ref pos);
        var s = System.Text.Encoding.UTF8.GetString(data, pos, len);
        pos += len;
        return s;
    }

    private static void SkipField(byte[] data, int wireType, ref int pos)
    {
        switch (wireType)
        {
            case 0: ReadVarint(data, ref pos); break; // varint
            case 1: pos += 8; break; // 64-bit
            case 2: var len = (int)ReadVarint(data, ref pos); pos += len; break; // length-delimited
            case 5: pos += 4; break; // 32-bit
        }
    }

    private static ulong ReadUint64BE(byte[] data, int offset)
    {
        return ((ulong)data[offset] << 56) | ((ulong)data[offset + 1] << 48) |
               ((ulong)data[offset + 2] << 40) | ((ulong)data[offset + 3] << 32) |
               ((ulong)data[offset + 4] << 24) | ((ulong)data[offset + 5] << 16) |
               ((ulong)data[offset + 6] << 8) | data[offset + 7];
    }

    private static uint ReadUint32BE(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) | data[offset + 3];
    }

    private static async Task ReadFullAsync(Stream s, byte[] buf, int count, CancellationToken ct)
    {
        int got = 0;
        while (got < count)
        {
            var n = await s.ReadAsync(buf.AsMemory(got, count - got), ct).ConfigureAwait(false);
            if (n == 0) throw new EndOfStreamException("Unexpected end of payload stream.");
            got += n;
        }
    }

    private sealed record ManifestPartition(string Name, List<InstallOperation> Operations)
    {
        public long DataOffset => Operations.Count > 0 ? Operations[0].DataOffset : 0;
        public long DataLength => Operations.Sum(o => o.DataLength);
        public long TotalOutputSize => Operations.Sum(o => o.DataLength);
    }

    private sealed record InstallOperation(int Type, long DataOffset, long DataLength, long DstOffset);
}
