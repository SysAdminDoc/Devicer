using System.IO;
using System.Security.Cryptography;

namespace Devicer.Core.Services;

/// <summary>
/// Decrypts a Samsung firmware blob (.enc2 / .enc4). The encryption is AES-128-ECB with
/// PKCS#7 padding on the final block — confirmed across every public FUS client
/// (samloader, Bifrost, samfirm).
///
/// <para>
/// We chunk the I/O at 4 KiB boundaries (matches the upstream protocol expectation that
/// the encrypted blob is a multiple of 16 bytes; in practice the blob is always a multiple
/// of 4 KiB).
/// </para>
/// </summary>
public static class FirmwareCipher
{
    private const int ChunkSize = 4096;

    /// <summary>
    /// Decrypts <paramref name="encryptedPath"/> to <paramref name="decryptedPath"/> using
    /// the provided AES-128 key. Returns the size of the decrypted output.
    /// </summary>
    /// <param name="progress">Reports decrypted-bytes-written, throttled to ~10 Hz internally.</param>
    public static async Task<long> DecryptFileAsync(
        string encryptedPath,
        string decryptedPath,
        byte[] key,
        IProgress<long>? progress,
        CancellationToken ct)
    {
        if (key.Length != 16) throw new ArgumentException("AES-128 firmware key must be 16 bytes", nameof(key));

        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None; // We strip the PKCS#7 pad ourselves on the last block.
        aes.Key = key;

        using var dec = aes.CreateDecryptor();

        await using var inFs = new FileStream(encryptedPath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, useAsync: true);
        await using var outFs = new FileStream(decryptedPath, FileMode.Create, FileAccess.Write, FileShare.Read, ChunkSize, useAsync: true);

        var inLen = inFs.Length;
        if (inLen % 16 != 0)
            throw new InvalidDataException($"Encrypted firmware size {inLen} is not a multiple of 16 — corrupt download or wrong endpoint.");

        var inBuf = new byte[ChunkSize];
        var outBuf = new byte[ChunkSize];
        long totalIn = 0, totalOut = 0;
        var lastReport = Environment.TickCount64;

        // Stream all but the final chunk through CryptoServiceProvider directly.
        while (true)
        {
            var read = await ReadFullAsync(inFs, inBuf, ct).ConfigureAwait(false);
            if (read == 0) break;

            // If this is the final block of the file, we must strip PKCS#7.
            var isFinal = totalIn + read >= inLen;

            if (read % 16 != 0)
                throw new InvalidDataException($"Short read of {read} bytes mid-stream — partial chunk not aligned to 16.");

            var written = dec.TransformBlock(inBuf, 0, read, outBuf, 0);

            if (isFinal)
            {
                // Verify + strip PKCS#7 padding on the trailing block. A weak check (just
                // the last byte) lets a wrong key silently produce garbage that "almost"
                // looks valid; a strict check requires every pad byte to equal the pad
                // count, which catches ~255/256 wrong-key cases on the final block alone.
                if (written < 16)
                    throw new InvalidDataException($"Decryption produced final block of {written} bytes — too small for PKCS#7. Wrong key or corrupt blob.");
                var pad = outBuf[written - 1];
                if (pad < 1 || pad > 16)
                    throw new InvalidDataException($"Decryption produced invalid PKCS#7 pad byte {pad} — wrong key or corrupt blob.");
                for (int i = written - pad; i < written; i++)
                {
                    if (outBuf[i] != pad)
                        throw new InvalidDataException($"PKCS#7 padding mismatch at offset {i} (expected 0x{pad:X2}, got 0x{outBuf[i]:X2}) — wrong key or corrupt blob.");
                }
                written -= pad;
            }

            await outFs.WriteAsync(outBuf.AsMemory(0, written), ct).ConfigureAwait(false);
            totalIn += read;
            totalOut += written;

            var now = Environment.TickCount64;
            if (now - lastReport > 100)
            {
                progress?.Report(totalIn);
                lastReport = now;
            }

            if (isFinal) break;
        }

        progress?.Report(totalIn);
        return totalOut;
    }

    private static async Task<int> ReadFullAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        int got = 0;
        while (got < buf.Length)
        {
            var n = await s.ReadAsync(buf.AsMemory(got), ct).ConfigureAwait(false);
            if (n == 0) break;
            got += n;
        }
        return got;
    }
}
