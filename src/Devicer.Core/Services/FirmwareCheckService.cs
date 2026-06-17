using System.Net.Http;
using System.Xml.Linq;
using Devicer.Core.Models;

namespace Devicer.Core.Services;

public sealed record LatestFirmware(FirmwareVersion Latest, IReadOnlyList<FirmwareVersion> UpgradeHistory);

public sealed record RegionalFirmwareResult(string Csc, LatestFirmware? Firmware, string? Error = null)
{
    public bool HasFirmware => Firmware is not null;
}

public interface IFirmwareCheckService
{
    /// <summary>
    /// Queries Samsung's public OTA-version endpoint for the latest published firmware
    /// for a given <paramref name="model"/> + <paramref name="csc"/> pair. No auth required.
    /// </summary>
    Task<LatestFirmware?> GetLatestAsync(string model, string csc, CancellationToken ct = default);

    /// <summary>
    /// Queries several CSC feeds for the same model. Region failures are isolated so one
    /// unavailable feed does not hide valid results from other regions.
    /// </summary>
    Task<IReadOnlyList<RegionalFirmwareResult>> GetLatestAcrossRegionsAsync(string model, IEnumerable<string> cscs, CancellationToken ct = default);
}

public sealed class FirmwareCheckService : IFirmwareCheckService, IDisposable
{
    // Samsung's public version-feed CDN. Returns XML with <latest> and <upgrade> nodes.
    // No authentication, no nonce — anyone can poll this.
    private const string FotaUrlTemplate = "https://fota-cloud-dn.ospserver.net/firmware/{0}/{1}/version.xml";

    private readonly HttpClient _http;

    public FirmwareCheckService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        // Samsung's CDN is permissive but a UA helps avoid edge-case rejections. Source the
        // version from the Core assembly so the UA can never go stale at release time.
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"Devicer/{HttpUserAgent.AssemblyVersion} (+https://github.com/SysAdminDoc/Devicer)");
    }

    public async Task<LatestFirmware?> GetLatestAsync(string model, string csc, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(csc))
            throw new ArgumentException("model and csc are required");

        var url = string.Format(FotaUrlTemplate, csc.Trim().ToUpperInvariant(), model.Trim().ToUpperInvariant());
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;

        var xml = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseVersionXml(xml);
    }

    public async Task<IReadOnlyList<RegionalFirmwareResult>> GetLatestAcrossRegionsAsync(string model, IEnumerable<string> cscs, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("model is required");

        var regions = NormalizeCscs(cscs).ToArray();
        if (regions.Length == 0)
            throw new ArgumentException("at least one CSC is required");

        var tasks = regions.Select(async csc =>
        {
            try
            {
                var firmware = await GetLatestAsync(model, csc, ct).ConfigureAwait(false);
                return new RegionalFirmwareResult(csc, firmware);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new RegionalFirmwareResult(csc, null, ex.Message);
            }
        });

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public static IReadOnlyList<string> ParseCscList(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return Array.Empty<string>();

        return NormalizeCscs(input.Split([',', ';', '|', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static IReadOnlyList<string> NormalizeCscs(IEnumerable<string> cscs) =>
        cscs
            .Select(c => c.Trim().ToUpperInvariant())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static LatestFirmware? ParseVersionXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return null; }

        var root = doc.Root;
        if (root is null) return null;

        // Two known schemas:
        //   <versioninfo><firmware><version><latest o="..">PDA/CSC/CP[/BOOT]</latest><upgrade>...</upgrade></version>...
        //   <versioninfo><firmware><version><latest o=".."/></version></firmware><release>...</release>
        var latestNode = root.Descendants("latest").FirstOrDefault();
        var latest = FirmwareVersion.TryParse(latestNode?.Value);
        if (latest is null) return null;

        var history = new List<FirmwareVersion>();
        foreach (var v in root.Descendants("upgrade").Elements("value"))
        {
            var fv = FirmwareVersion.TryParse(v.Value);
            if (fv is not null) history.Add(fv);
        }

        return new LatestFirmware(latest, history);
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
