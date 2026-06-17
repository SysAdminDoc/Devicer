using Devicer.Core.Services;

namespace Devicer.Core.Tests;

public class PayloadExtractTests
{
    [Fact]
    public async Task ListPartitions_rejects_non_payload_file()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            // Write 24+ bytes with wrong magic so it gets past the header read
            await File.WriteAllBytesAsync(tmp, new byte[32]);
            var svc = new PayloadExtractService();
            await Assert.ThrowsAsync<InvalidDataException>(() => svc.ListPartitionsAsync(tmp));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task ListPartitions_rejects_empty_file()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var svc = new PayloadExtractService();
            await Assert.ThrowsAsync<EndOfStreamException>(() => svc.ListPartitionsAsync(tmp));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task ExtractPartition_rejects_missing_partition()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            // Write a minimal valid CrAU header with empty manifest
            using (var fs = File.Create(tmp))
            using (var w = new BinaryWriter(fs))
            {
                // CrAU magic
                w.Write((byte)'C'); w.Write((byte)'r'); w.Write((byte)'A'); w.Write((byte)'U');
                // Version (uint64 BE) = 2
                w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); w.Write((byte)2);
                // Manifest size (uint64 BE) = 0
                w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                // Metadata signature size (uint32 BE) = 0
                w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
            }

            var svc = new PayloadExtractService();
            var partitions = await svc.ListPartitionsAsync(tmp);
            Assert.Empty(partitions);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
