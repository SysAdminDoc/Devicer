using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Devicer.Core.Models;

namespace Devicer.Core.Services;

/// <summary>
/// Progress report for the orchestrated firmware-download pipeline. Phase tells the UI
/// whether we're authenticating, fetching metadata, downloading, decrypting, or done.
/// </summary>
public enum FirmwarePhase
{
    Authenticating,
    FetchingMetadata,
    Downloading,
    Decrypting,
    Done,
    Cancelled,
    Failed,
}

public sealed record FirmwareProgress(
    FirmwarePhase Phase,
    long BytesProcessed,
    long? TotalBytes,
    string? Message = null
)
{
    public double? FractionComplete =>
        TotalBytes is { } t && t > 0 ? Math.Clamp(BytesProcessed / (double)t, 0, 1) : null;
}

public sealed record FirmwareDownloadResult(
    string EncryptedPath,
    string DecryptedPath,
    long EncryptedSize,
    long DecryptedSize,
    string EncryptedSha256,
    FirmwareBinaryInfo Info
);

public interface IFirmwareDownloadService
{
    /// <summary>
    /// Hits NF_DownloadBinaryInform.do for the specified target version.
    /// <para>
    /// As of late 2024 Samsung's FUS rejects the legacy fake-IMEI <c>"00000000000000"</c>
    /// (FUS Status 408 / Authentication Failed). A real 14-15 digit IMEI matching the
    /// model+region pair is required.
    /// </para>
    /// </summary>
    Task<FirmwareBinaryInfo> GetBinaryInfoAsync(string model, string region, string targetVersion, string imei, CancellationToken ct = default);

    /// <summary>
    /// Full pipeline: authenticate → BinaryInform → stream-download → SHA256-verify → decrypt.
    /// Pulls into <c>%LOCALAPPDATA%\Devicer\firmware\&lt;model&gt;_&lt;region&gt;_&lt;version&gt;\</c>.
    /// </summary>
    Task<FirmwareDownloadResult> DownloadAndDecryptAsync(
        string model,
        string region,
        string targetVersion,
        string imei,
        IProgress<FirmwareProgress>? progress,
        CancellationToken ct = default);
}

public sealed class FirmwareDownloadService : IFirmwareDownloadService, IDisposable
{
    private readonly FusClient _fus;
    private readonly FirmwareCache _cache;

    public FirmwareDownloadService(FusClient? fus = null, FirmwareCache? cache = null)
    {
        _fus = fus ?? new FusClient();
        _cache = cache ?? new FirmwareCache();
    }

    public async Task<FirmwareBinaryInfo> GetBinaryInfoAsync(string model, string region, string targetVersion, string imei, CancellationToken ct = default)
    {
        await _fus.EnsureSessionAsync(ct).ConfigureAwait(false);
        var nonce = _fus.Nonce ?? throw new InvalidOperationException("FUS session not established");

        var xml = BuildBinaryInformXml(model, region, targetVersion, imei, nonce);
        var resp = await _fus.PostXmlAsync("/NF_DownloadBinaryInform.do", xml, ct).ConfigureAwait(false);

        return ParseBinaryInformResponse(resp);
    }

    public async Task<FirmwareDownloadResult> DownloadAndDecryptAsync(
        string model,
        string region,
        string targetVersion,
        string imei,
        IProgress<FirmwareProgress>? progress,
        CancellationToken ct = default)
    {
        progress?.Report(new FirmwareProgress(FirmwarePhase.Authenticating, 0, null, "Establishing FUS session…"));

        FirmwareBinaryInfo info;
        try
        {
            await _fus.EnsureSessionAsync(ct).ConfigureAwait(false);
            progress?.Report(new FirmwareProgress(FirmwarePhase.FetchingMetadata, 0, null, $"Fetching binary metadata for {model} / {region} / {targetVersion}…"));
            info = await GetBinaryInfoAsync(model, region, targetVersion, imei, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            progress?.Report(new FirmwareProgress(FirmwarePhase.Cancelled, 0, null, "Cancelled."));
            throw;
        }

        var folder = _cache.PrepareFolder(model, region, targetVersion);
        var encPath = Path.Combine(folder, info.BinaryName);
        var decName = info.BinaryName.EndsWith(".enc4", StringComparison.OrdinalIgnoreCase) || info.BinaryName.EndsWith(".enc2", StringComparison.OrdinalIgnoreCase)
            ? info.BinaryName[..info.BinaryName.LastIndexOf('.')]
            : info.BinaryName + ".dec";
        var decPath = Path.Combine(folder, decName);

        // Resume support: if a partial .enc file exists and is smaller than expected, range-resume from there.
        long resumeFrom = 0;
        if (File.Exists(encPath))
        {
            var existing = new FileInfo(encPath).Length;
            if (existing < info.BinaryByteSize) resumeFrom = existing;
            else if (existing == info.BinaryByteSize) resumeFrom = info.BinaryByteSize; // already complete
        }

        if (resumeFrom < info.BinaryByteSize)
        {
            progress?.Report(new FirmwareProgress(FirmwarePhase.Downloading, resumeFrom, info.BinaryByteSize,
                resumeFrom > 0 ? $"Resuming download at {FormatBytes(resumeFrom)} / {FormatBytes(info.BinaryByteSize)}…" : $"Downloading {FormatBytes(info.BinaryByteSize)}…"));

            await DownloadEncryptedAsync(info, encPath, resumeFrom, progress, ct).ConfigureAwait(false);
        }
        else
        {
            progress?.Report(new FirmwareProgress(FirmwarePhase.Downloading, info.BinaryByteSize, info.BinaryByteSize, "Download already complete."));
        }

        // SHA256 over the encrypted blob (we don't have an authoritative hash from the server, so this
        // is informational — useful for cache-hit detection and integrity checks across resumes).
        progress?.Report(new FirmwareProgress(FirmwarePhase.Downloading, info.BinaryByteSize, info.BinaryByteSize, "Hashing encrypted blob…"));
        var sha = await ComputeSha256Async(encPath, ct).ConfigureAwait(false);

        progress?.Report(new FirmwareProgress(FirmwarePhase.Decrypting, 0, info.BinaryByteSize, "Decrypting firmware…"));
        var key = info.IsV4
            ? FusCrypto.DeriveFirmwareKeyV4(info.LatestFwVersion, info.LogicValueFactory)
            : FusCrypto.DeriveFirmwareKeyV2(region, model, targetVersion);

        var decryptedSize = await FirmwareCipher.DecryptFileAsync(encPath, decPath,
            key,
            new Progress<long>(bytes => progress?.Report(new FirmwareProgress(FirmwarePhase.Decrypting, bytes, info.BinaryByteSize))),
            ct).ConfigureAwait(false);

        // Index the result.
        _cache.WriteIndex(folder, new FirmwareCache.IndexRecord(
            Model: model,
            Region: region,
            Version: targetVersion,
            BinaryName: info.BinaryName,
            EncryptedSize: info.BinaryByteSize,
            DecryptedSize: decryptedSize,
            EncryptedSha256: sha,
            CompletedUtc: DateTimeOffset.UtcNow));

        progress?.Report(new FirmwareProgress(FirmwarePhase.Done, decryptedSize, decryptedSize, $"Done. Decrypted to {decPath}"));
        return new FirmwareDownloadResult(encPath, decPath, info.BinaryByteSize, decryptedSize, sha, info);
    }

    private async Task DownloadEncryptedAsync(FirmwareBinaryInfo info, string outPath, long resumeFrom,
        IProgress<FirmwareProgress>? progress, CancellationToken ct)
    {
        using var resp = await _fus.StartDownloadAsync(info.BinaryName, resumeFrom > 0 ? resumeFrom : null, ct).ConfigureAwait(false);
        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = new FileStream(outPath,
            resumeFrom > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 1 << 16,
            useAsync: true);

        var buffer = new byte[1 << 16];
        long total = resumeFrom;
        var lastReport = Environment.TickCount64;
        while (true)
        {
            var n = await src.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (n <= 0) break;
            await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            total += n;
            // Throttle progress to ~10 Hz to avoid flooding the dispatcher.
            var now = Environment.TickCount64;
            if (now - lastReport > 100)
            {
                progress?.Report(new FirmwareProgress(FirmwarePhase.Downloading, total, info.BinaryByteSize));
                lastReport = now;
            }
        }
        progress?.Report(new FirmwareProgress(FirmwarePhase.Downloading, total, info.BinaryByteSize));
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        using var sha = SHA256.Create();
        var buf = new byte[1 << 16];
        int n;
        while ((n = await fs.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
            sha.TransformBlock(buf, 0, n, null, 0);
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
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

    internal static string BuildBinaryInformXml(string model, string region, string fwv, string imei, string decodedNonce)
    {
        // Schema is fixed by the FUS server. As of late 2024 Samsung requires DEVICE_IMEI_PUSH
        // to be a real device IMEI (the legacy "0000…" fake yields FUS Status 408).
        var logicCheck = FusCrypto.GetLogicCheck(fwv, decodedNonce);
        var doc = new XDocument(
            new XElement("FUSMsg",
                new XElement("FUSHdr",
                    new XElement("ProtoVer", "1.0")),
                new XElement("FUSBody",
                    new XElement("Put",
                        Field("ACCESS_MODE", "2"),
                        Field("BINARY_NATURE", "1"),
                        Field("CLIENT_PRODUCT", "Smart Switch"),
                        Field("DEVICE_IMEI_PUSH", imei),
                        Field("DEVICE_FW_VERSION", fwv),
                        Field("DEVICE_LOCAL_CODE", region),
                        Field("DEVICE_MODEL_NAME", model),
                        Field("LOGIC_CHECK", logicCheck)
                    )
                )
            )
        );
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + doc.ToString(SaveOptions.DisableFormatting);
    }

    private static XElement Field(string name, string data) =>
        new XElement(name, new XElement("Data", data));

    internal static FirmwareBinaryInfo ParseBinaryInformResponse(string xml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (Exception ex) { throw new FusProtocolException("BinaryInform: server returned non-XML body", ex); }

        var status = doc.Descendants("Status").FirstOrDefault()?.Value;
        if (!string.IsNullOrEmpty(status) && status != "200")
            throw new FusProtocolException($"BinaryInform: server returned status {status}. The model+region+version triple is probably invalid.");

        string Get(string name)
            => doc.Descendants(name).FirstOrDefault()?.Element("Data")?.Value
               ?? throw new FusProtocolException($"BinaryInform: missing field '{name}' in response");

        var binaryName = Get("BINARY_NAME");
        var binaryByteSizeStr = Get("BINARY_BYTE_SIZE");
        if (!long.TryParse(binaryByteSizeStr, out var size) || size <= 0)
            throw new FusProtocolException($"BinaryInform: invalid BINARY_BYTE_SIZE '{binaryByteSizeStr}'");

        var modelPath = doc.Descendants("MODEL_PATH").FirstOrDefault()?.Element("Data")?.Value ?? string.Empty;
        var fwver = doc.Descendants("LATEST_FW_VERSION").FirstOrDefault()?.Element("Data")?.Value ?? string.Empty;
        var logicVal = doc.Descendants("LOGIC_VALUE_FACTORY").FirstOrDefault()?.Element("Data")?.Value ?? string.Empty;

        return new FirmwareBinaryInfo
        {
            BinaryName = binaryName,
            BinaryByteSize = size,
            ModelPath = modelPath,
            LatestFwVersion = fwver,
            LogicValueFactory = logicVal,
        };
    }

    public void Dispose()
    {
        _fus.Dispose();
    }
}
