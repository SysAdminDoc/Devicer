using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Devicer.Core.Models;

namespace Devicer.Core.Services;

/// <summary>
/// LineageOS v1 update API. URL: <c>https://download.lineageos.org/api/v1/&lt;codename&gt;/&lt;romtype&gt;/*</c>.
/// Returns <c>{"response":[…]}</c> with build entries: filename, datetime, size, url, id (sha256), version, romtype.
/// We probe nightly + weekly because both are active across the LineageOS device matrix.
/// </summary>
public sealed class LineageOsRomSource : IRomSource, IDisposable
{
    private const string ApiTemplate = "https://download.lineageos.org/api/v1/{0}/{1}/*";
    private static readonly string[] RomTypes = { "nightly", "weekly" };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public LineageOsRomSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _ownsHttp = http is null;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Devicer/0.4 (+https://github.com/SysAdminDoc/Devicer)");
    }

    public RomSource Source => RomSource.LineageOS;

    public async Task<IReadOnlyList<RomEntry>> SearchAsync(string codename, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(codename)) return Array.Empty<RomEntry>();
        var slug = codename.Trim().ToLowerInvariant();

        var entries = new List<RomEntry>();
        foreach (var type in RomTypes)
        {
            var url = string.Format(ApiTemplate, slug, type);
            try
            {
                var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) continue;
                var doc = await resp.Content.ReadFromJsonAsync<LineageResponse>(LineageJsonOpts, ct).ConfigureAwait(false);
                if (doc?.Response is null) continue;

                foreach (var b in doc.Response)
                {
                    if (string.IsNullOrWhiteSpace(b.Url) || string.IsNullOrWhiteSpace(b.Filename)) continue;
                    if (!Uri.TryCreate(b.Url, UriKind.Absolute, out var dl)) continue;
                    entries.Add(new RomEntry
                    {
                        Source = RomSource.LineageOS,
                        Kind = b.Romtype switch
                        {
                            "nightly" => RomKind.Nightly,
                            "weekly" => RomKind.Weekly,
                            "stable" => RomKind.Stable,
                            _ => RomKind.Unknown,
                        },
                        Codename = slug,
                        Version = b.Version ?? string.Empty,
                        BuildDate = DateTimeOffset.FromUnixTimeSeconds(b.Datetime),
                        SizeBytes = b.Size,
                        FileName = b.Filename,
                        DownloadUrl = dl,
                        Sha256 = b.Id, // The id field IS the sha256 in v1.
                    });
                }
            }
            catch (HttpRequestException) { /* per-source transport failure — skip type */ }
            catch (JsonException) { /* malformed response — skip */ }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { /* per-call timeout */ }
        }

        // Sort newest first.
        entries.Sort((a, b) => b.BuildDate.CompareTo(a.BuildDate));
        return entries;
    }

    private static readonly JsonSerializerOptions LineageJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record LineageResponse([property: JsonPropertyName("response")] List<LineageBuild>? Response);

    private sealed record LineageBuild(
        [property: JsonPropertyName("filename")] string? Filename,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("datetime")] long Datetime,
        [property: JsonPropertyName("romtype")] string? Romtype,
        [property: JsonPropertyName("version")] string? Version
    );

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
