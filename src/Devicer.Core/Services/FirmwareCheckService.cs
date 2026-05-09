using System.Net.Http;
using System.Xml.Linq;
using Devicer.Core.Models;

namespace Devicer.Core.Services;

public sealed record LatestFirmware(FirmwareVersion Latest, IReadOnlyList<FirmwareVersion> UpgradeHistory);

public interface IFirmwareCheckService
{
    /// <summary>
    /// Queries Samsung's public OTA-version endpoint for the latest published firmware
    /// for a given <paramref name="model"/> + <paramref name="csc"/> pair. No auth required.
    /// </summary>
    Task<LatestFirmware?> GetLatestAsync(string model, string csc, CancellationToken ct = default);
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
