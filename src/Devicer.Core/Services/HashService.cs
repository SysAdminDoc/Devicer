using System.IO;
using System.Security.Cryptography;

namespace Devicer.Core.Services;

public interface IHashService
{
    Task<string> ComputeSha256Async(string path, CancellationToken ct = default);
    Task<string> ComputeSha256Async(string path, IProgress<long>? progress, CancellationToken ct = default);
}

public sealed class HashService : IHashService
{
    public Task<string> ComputeSha256Async(string path, CancellationToken ct = default)
        => ComputeSha256Async(path, null, ct);

    public async Task<string> ComputeSha256Async(string path, IProgress<long>? progress, CancellationToken ct = default)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        using var sha = SHA256.Create();
        var buf = new byte[1 << 16];
        long hashed = 0;
        int n;
        while ((n = await fs.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            sha.TransformBlock(buf, 0, n, null, 0);
            hashed += n;
            progress?.Report(hashed);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }
}
