using System.IO;
using System.Net.Http;
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
    private readonly IHashService _hash;

    public FirmwareDownloadService(IHashService? hash = null, FusClient? fus = null, FirmwareCache? cache = null)
    {
        _fus = fus ?? new FusClient();
        _cache = cache ?? new FirmwareCache();
        _hash = hash ?? new HashService();
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
        DevicerLog.Section($"Download {model}/{region} {targetVersion} (IMEI {(imei.Length >= 8 ? imei[..8] + "…" : imei)})");
        progress?.Report(new FirmwareProgress(FirmwarePhase.Authenticating, 0, null, "Establishing FUS session…"));

        FirmwareBinaryInfo info;
        try
        {
            await _fus.EnsureSessionAsync(ct).ConfigureAwait(false);
            progress?.Report(new FirmwareProgress(FirmwarePhase.FetchingMetadata, 0, null, $"Fetching binary metadata for {model} / {region} / {targetVersion}…"));
            info = await GetBinaryInfoAsync(model, region, targetVersion, imei, ct).ConfigureAwait(false);
            DevicerLog.Info("Download", $"BinaryInfo parsed: BinaryName='{info.BinaryName}', ModelPath='{info.ModelPath}', Size={info.BinaryByteSize:N0} bytes, IsV4={info.IsV4}");
            DevicerLog.Info("Download", $"  LatestFwVersion='{info.LatestFwVersion}', LogicValueFactory='{info.LogicValueFactory}'");
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

        var key = info.IsV4
            ? FusCrypto.DeriveFirmwareKeyV4(info.LatestFwVersion, info.LogicValueFactory)
            : FusCrypto.DeriveFirmwareKeyV2(region, model, targetVersion);

        long decryptedSize;
        string sha;

        if (resumeFrom > 0 && resumeFrom < info.BinaryByteSize)
        {
            progress?.Report(new FirmwareProgress(FirmwarePhase.Downloading, resumeFrom, info.BinaryByteSize,
                $"Resuming download at {FormatBytes(resumeFrom)} / {FormatBytes(info.BinaryByteSize)}…"));
            await BinaryInitForMassAsync(info, ct).ConfigureAwait(false);
            await DownloadEncryptedAsync(info, encPath, resumeFrom, progress, ct).ConfigureAwait(false);

            progress?.Report(new FirmwareProgress(FirmwarePhase.Downloading, info.BinaryByteSize, info.BinaryByteSize, "Hashing encrypted blob…"));
            sha = await _hash.ComputeSha256Async(encPath, ct).ConfigureAwait(false);

            progress?.Report(new FirmwareProgress(FirmwarePhase.Decrypting, 0, info.BinaryByteSize, "Decrypting firmware…"));
            decryptedSize = await FirmwareCipher.DecryptFileAsync(encPath, decPath, key,
                new Progress<long>(bytes => progress?.Report(new FirmwareProgress(FirmwarePhase.Decrypting, bytes, info.BinaryByteSize))),
                ct).ConfigureAwait(false);
        }
        else if (resumeFrom >= info.BinaryByteSize)
        {
            progress?.Report(new FirmwareProgress(FirmwarePhase.Downloading, info.BinaryByteSize, info.BinaryByteSize, "Download already complete."));
            sha = await _hash.ComputeSha256Async(encPath, ct).ConfigureAwait(false);

            progress?.Report(new FirmwareProgress(FirmwarePhase.Decrypting, 0, info.BinaryByteSize, "Decrypting firmware…"));
            decryptedSize = await FirmwareCipher.DecryptFileAsync(encPath, decPath, key,
                new Progress<long>(bytes => progress?.Report(new FirmwareProgress(FirmwarePhase.Decrypting, bytes, info.BinaryByteSize))),
                ct).ConfigureAwait(false);
        }
        else
        {
            progress?.Report(new FirmwareProgress(FirmwarePhase.Downloading, 0, info.BinaryByteSize,
                $"Downloading + decrypting {FormatBytes(info.BinaryByteSize)} (streaming)…"));
            await BinaryInitForMassAsync(info, ct).ConfigureAwait(false);
            (decryptedSize, sha) = await DownloadAndDecryptStreamingAsync(info, decPath, key, progress, ct).ConfigureAwait(false);
        }

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

    private async Task BinaryInitForMassAsync(FirmwareBinaryInfo info, CancellationToken ct)
    {
        var nonce = _fus.Nonce ?? throw new InvalidOperationException("FUS session not established");
        var fwvSlice = ExtractFwvSlice(info.BinaryName);
        var logicCheck = FusCrypto.GetLogicCheck(fwvSlice, nonce);

        var doc = new XDocument(
            new XElement("FUSMsg",
                new XElement("FUSHdr", new XElement("ProtoVer", "1.0")),
                new XElement("FUSBody",
                    new XElement("Put",
                        Field("BINARY_FILE_NAME", info.BinaryName),
                        Field("LOGIC_CHECK", logicCheck)
                    )
                )
            )
        );
        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + doc.ToString(SaveOptions.DisableFormatting);
        var resp = await _fus.PostXmlAsync("/NF_DownloadBinaryInitForMass.do", xml, ct).ConfigureAwait(false);
        // Status check: any non-200 in the response body kills the download.
        var status = System.Text.RegularExpressions.Regex.Match(resp, @"<Status>(\d+)</Status>");
        if (status.Success && status.Groups[1].Value != "200")
            throw new FusProtocolException($"BinaryInitForMass: server returned status {status.Groups[1].Value}");
    }

    /// <summary>
    /// Computes the 16-char filename slice fed into <c>LOGIC_CHECK</c>. The canonical FUS
    /// algorithm (samloader, Bifrost, samfirm) is <c>filename.split(".")[0][-16:]</c> — split
    /// on the FIRST dot and take the trailing 16 chars of that chunk. Using
    /// <c>LastIndexOf('.')</c> instead silently produces a wrong slice for any filename with
    /// multiple dots (which every modern firmware name has, e.g. <c>SW_….zip.enc4</c>) and the
    /// server rejects the InitForMass call.
    /// </summary>
    public static string ExtractFwvSlice(string binaryName)
    {
        if (string.IsNullOrEmpty(binaryName))
            throw new FusProtocolException("BinaryInitForMass: empty binary name from server.");
        var stem = binaryName;
        var firstDot = stem.IndexOf('.');
        if (firstDot > 0) stem = stem[..firstDot];
        if (stem.Length < 16)
            throw new FusProtocolException($"BinaryInitForMass: filename stem '{stem}' is shorter than 16 chars; cannot compute logic check.");
        return stem[^16..];
    }

    private async Task<(long DecryptedSize, string EncryptedSha256)> DownloadAndDecryptStreamingAsync(
        FirmwareBinaryInfo info, string decPath, byte[] key,
        IProgress<FirmwareProgress>? progress, CancellationToken ct)
    {
        var remotePath = (info.ModelPath ?? string.Empty) + info.BinaryName;
        using var resp = await _fus.StartDownloadAsync(remotePath, null, ct).ConfigureAwait(false);
        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        var decryptedSize = await FirmwareCipher.DecryptStreamAsync(
            src, decPath, key, info.BinaryByteSize,
            new Progress<long>(bytes => progress?.Report(new FirmwareProgress(FirmwarePhase.Downloading, bytes, info.BinaryByteSize))),
            ct).ConfigureAwait(false);

        return (decryptedSize, "streaming-no-enc-hash");
    }

    private async Task DownloadEncryptedAsync(FirmwareBinaryInfo info, string outPath, long resumeFrom,
        IProgress<FirmwareProgress>? progress, CancellationToken ct)
    {
        // The cloud-download endpoint expects the full server-relative path:
        // ?file=<MODEL_PATH><BINARY_NAME>. Without the MODEL_PATH prefix, Samsung's CDN
        // returns HTTP 403.
        var remotePath = (info.ModelPath ?? string.Empty) + info.BinaryName;
        using var resp = await _fus.StartDownloadAsync(remotePath, resumeFrom > 0 ? resumeFrom : null, ct).ConfigureAwait(false);
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
            throw new FusProtocolException($"BinaryInform: server returned status {status}. The model+region+version triple is probably invalid.", xml);

        // Field-name resolution. Samsung's response schema uses BINARY_NAME on most paths but
        // some CDN nodes return BINARY_FILE_NAME. The size is BINARY_BYTE_SIZE everywhere.
        string? GetFirst(params string[] names)
        {
            foreach (var n in names)
            {
                var v = doc.Descendants(n).FirstOrDefault()?.Element("Data")?.Value;
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            return null;
        }

        var binaryName = GetFirst("BINARY_NAME", "BINARY_FILE_NAME")
            ?? throw new FusProtocolException("BinaryInform: missing BINARY_NAME / BINARY_FILE_NAME in response", xml);
        // Defend against BINARY_NATURE (value "1"/"0") leaking through.
        if (binaryName.Length < 8 || !binaryName.Contains('.'))
            throw new FusProtocolException(
                $"BinaryInform: BINARY_NAME='{binaryName}' is too short to be a real firmware filename — server response shape changed.",
                xml);

        var binaryByteSizeStr = GetFirst("BINARY_BYTE_SIZE", "BINARY_TOTAL_BYTE_COUNT")
            ?? throw new FusProtocolException("BinaryInform: missing BINARY_BYTE_SIZE in response", xml);
        if (!long.TryParse(binaryByteSizeStr, out var size) || size <= 0)
            throw new FusProtocolException($"BinaryInform: invalid BINARY_BYTE_SIZE '{binaryByteSizeStr}'", xml);

        var modelPath = GetFirst("MODEL_PATH", "BINARY_MODEL_PATH") ?? string.Empty;
        // Normalize: ensure it starts and ends with /.
        if (modelPath.Length > 0)
        {
            if (!modelPath.StartsWith('/')) modelPath = "/" + modelPath;
            if (!modelPath.EndsWith('/')) modelPath += "/";
        }

        var fwver = GetFirst("LATEST_FW_VERSION", "DEVICE_LATEST_VERSION") ?? string.Empty;
        var logicVal = GetFirst("LOGIC_VALUE_FACTORY", "LOGIC_VALUE_HOME") ?? string.Empty;

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
