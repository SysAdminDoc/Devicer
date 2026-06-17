using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Devicer.Core.Models;

namespace Devicer.Core.Services;

public enum RomDownloadPhase
{
    Downloading,
    Verifying,
    Done,
    Cancelled,
    Failed,
}

public sealed record RomDownloadProgress(
    RomDownloadPhase Phase,
    long BytesDownloaded,
    long? TotalBytes,
    string? Message = null
)
{
    public double? FractionComplete =>
        TotalBytes is { } t && t > 0 ? Math.Clamp(BytesDownloaded / (double)t, 0, 1) : null;
}

public sealed record RomDownloadResult(
    string LocalPath,
    long SizeBytes,
    bool HashVerified,
    string? HashAlgorithm,
    string? ExpectedHash,
    string? ActualHash
);

public interface IRomDownloadService : IDisposable
{
    Task<RomDownloadResult> DownloadAsync(RomEntry entry, IProgress<RomDownloadProgress>? progress, CancellationToken ct = default);
    string GetCachePath(RomEntry entry);
    bool IsCached(RomEntry entry);
}

public sealed class RomDownloadService : IRomDownloadService
{
    private readonly HttpClient _http;
    private readonly string _root;

    public RomDownloadService(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Devicer", "roms");
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Devicer/1.2 (Windows)");
        _http.Timeout = TimeSpan.FromHours(4);
    }

    public string GetCachePath(RomEntry entry)
    {
        var slug = SafeSlug(entry.Codename);
        return Path.Combine(_root, slug, entry.FileName);
    }

    public bool IsCached(RomEntry entry)
    {
        var path = GetCachePath(entry);
        if (!File.Exists(path)) return false;
        if (entry.SizeBytes > 0)
            return new FileInfo(path).Length == entry.SizeBytes;
        return true;
    }

    public async Task<RomDownloadResult> DownloadAsync(
        RomEntry entry, IProgress<RomDownloadProgress>? progress, CancellationToken ct = default)
    {
        DevicerLog.Section($"ROM download: {entry.FileName} from {entry.Source}");

        var localPath = GetCachePath(entry);
        var dir = Path.GetDirectoryName(localPath)!;
        Directory.CreateDirectory(dir);

        long resumeFrom = 0;
        if (File.Exists(localPath))
        {
            var existingSize = new FileInfo(localPath).Length;
            if (entry.SizeBytes > 0 && existingSize == entry.SizeBytes)
            {
                DevicerLog.Info("RomDL", "File already cached, skipping download");
                progress?.Report(new RomDownloadProgress(RomDownloadPhase.Downloading, existingSize, existingSize, "Already cached."));
            }
            else if (entry.SizeBytes > 0 && existingSize < entry.SizeBytes)
            {
                resumeFrom = existingSize;
            }
            else
            {
                File.Delete(localPath);
            }
        }

        var totalExpected = entry.SizeBytes > 0 ? entry.SizeBytes : (long?)null;

        if (!File.Exists(localPath) || resumeFrom > 0)
        {
            var msg = resumeFrom > 0
                ? $"Resuming at {FormatBytes(resumeFrom)}…"
                : $"Downloading {entry.FileName}…";
            progress?.Report(new RomDownloadProgress(RomDownloadPhase.Downloading, resumeFrom, totalExpected, msg));

            using var req = new HttpRequestMessage(HttpMethod.Get, entry.DownloadUrl);
            if (resumeFrom > 0)
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeFrom, null);

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (resumeFrom > 0 && resp.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                resumeFrom = 0;
                File.Delete(localPath);
                resp.Dispose();
                return await DownloadAsync(entry, progress, ct).ConfigureAwait(false);
            }

            resp.EnsureSuccessStatusCode();

            if (totalExpected is null && resp.Content.Headers.ContentLength is { } cl)
                totalExpected = resumeFrom + cl;

            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(localPath,
                resumeFrom > 0 ? FileMode.Append : FileMode.Create,
                FileAccess.Write, FileShare.None,
                bufferSize: 1 << 16, useAsync: true);

            var buffer = new byte[1 << 16];
            long total = resumeFrom;
            var lastReport = Environment.TickCount64;
            while (true)
            {
                var n = await src.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                if (n <= 0) break;
                await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                total += n;
                var now = Environment.TickCount64;
                if (now - lastReport > 100)
                {
                    progress?.Report(new RomDownloadProgress(RomDownloadPhase.Downloading, total, totalExpected));
                    lastReport = now;
                }
            }
            progress?.Report(new RomDownloadProgress(RomDownloadPhase.Downloading, total, totalExpected ?? total, "Download complete."));
        }

        var fileSize = new FileInfo(localPath).Length;

        string? algo = null;
        string? expectedHash = null;
        string? actualHash = null;
        bool verified = false;

        if (!string.IsNullOrWhiteSpace(entry.Sha256))
        {
            algo = "SHA256";
            expectedHash = entry.Sha256;
            progress?.Report(new RomDownloadProgress(RomDownloadPhase.Verifying, 0, fileSize, "Verifying SHA256…"));
            actualHash = await ComputeSha256Async(localPath, progress, fileSize, ct).ConfigureAwait(false);
            verified = string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
        }
        else if (!string.IsNullOrWhiteSpace(entry.Md5))
        {
            algo = "MD5";
            expectedHash = entry.Md5;
            progress?.Report(new RomDownloadProgress(RomDownloadPhase.Verifying, 0, fileSize, "Verifying MD5…"));
            actualHash = await ComputeMd5Async(localPath, progress, fileSize, ct).ConfigureAwait(false);
            verified = string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
        }

        if (algo is not null)
        {
            DevicerLog.Info("RomDL", $"{algo} expected={expectedHash}, actual={actualHash}, match={verified}");
            if (!verified)
            {
                DevicerLog.Warn("RomDL", $"Hash mismatch! File may be corrupt.");
                progress?.Report(new RomDownloadProgress(RomDownloadPhase.Failed, fileSize, fileSize,
                    $"{algo} mismatch: expected {expectedHash}, got {actualHash}. File may be corrupt."));
            }
        }

        progress?.Report(new RomDownloadProgress(RomDownloadPhase.Done, fileSize, fileSize,
            verified ? $"Verified ({algo})." : algo is null ? "Done (no hash to verify)." : $"{algo} mismatch!"));

        return new RomDownloadResult(localPath, fileSize, verified, algo, expectedHash, actualHash);
    }

    private static async Task<string> ComputeSha256Async(
        string path, IProgress<RomDownloadProgress>? progress, long total, CancellationToken ct)
    {
        using var hash = SHA256.Create();
        return await HashFileAsync(hash, path, progress, total, ct).ConfigureAwait(false);
    }

    private static async Task<string> ComputeMd5Async(
        string path, IProgress<RomDownloadProgress>? progress, long total, CancellationToken ct)
    {
        #pragma warning disable CA5351
        using var hash = MD5.Create();
        #pragma warning restore CA5351
        return await HashFileAsync(hash, path, progress, total, ct).ConfigureAwait(false);
    }

    private static async Task<string> HashFileAsync(
        HashAlgorithm hash, string path, IProgress<RomDownloadProgress>? progress, long total, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        var buf = new byte[1 << 16];
        long hashed = 0;
        var lastReport = Environment.TickCount64;
        int n;
        while ((n = await fs.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            hash.TransformBlock(buf, 0, n, null, 0);
            hashed += n;
            var now = Environment.TickCount64;
            if (now - lastReport > 100)
            {
                progress?.Report(new RomDownloadProgress(RomDownloadPhase.Verifying, hashed, total));
                lastReport = now;
            }
        }
        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(hash.Hash!).ToLowerInvariant();
    }

    private static string FormatBytes(long bytes)
    {
        const long k = 1024, m = k * 1024, g = m * 1024;
        return bytes switch
        {
            >= g => $"{bytes / (double)g:0.00} GB",
            >= m => $"{bytes / (double)m:0.0} MB",
            >= k => $"{bytes / (double)k:0.0} KB",
            _ => $"{bytes} B",
        };
    }

    private static string SafeSlug(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = input.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    public void Dispose() => _http.Dispose();
}
