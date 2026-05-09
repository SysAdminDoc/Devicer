using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Devicer.Core.Models;

namespace Devicer.Core.Services;

/// <summary>
/// crDroid OTA repository on GitHub. URL pattern:
///   <c>https://raw.githubusercontent.com/crdroidandroid/android_vendor_crDroidOTA/&lt;branch&gt;/&lt;codename&gt;.json</c>.
/// We probe a fan of supported branches (16.0 / 15.0 / 14.0) so users still on older
/// Android targets see entries.
/// </summary>
public sealed class CrDroidRomSource : IRomSource, IDisposable
{
    private const string UrlTemplate = "https://raw.githubusercontent.com/crdroidandroid/android_vendor_crDroidOTA/{0}/{1}.json";
    private static readonly string[] Branches = { "16.0", "15.0", "14.0" };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public CrDroidRomSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _ownsHttp = http is null;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"Devicer/{HttpUserAgent.AssemblyVersion} (+https://github.com/SysAdminDoc/Devicer)");
    }

    public RomSource Source => RomSource.CrDroid;

    public async Task<IReadOnlyList<RomEntry>> SearchAsync(string codename, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(codename)) return Array.Empty<RomEntry>();
        var slug = codename.Trim().ToLowerInvariant();

        var entries = new List<RomEntry>();
        foreach (var branch in Branches)
        {
            var url = string.Format(UrlTemplate, branch, slug);
            try
            {
                var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) continue;
                var doc = await resp.Content.ReadFromJsonAsync<CrDroidResponse>(CrDroidJsonOpts, ct).ConfigureAwait(false);
                if (doc?.Response is null) continue;

                foreach (var b in doc.Response)
                {
                    if (string.IsNullOrWhiteSpace(b.Download) || string.IsNullOrWhiteSpace(b.Filename)) continue;
                    if (!Uri.TryCreate(b.Download, UriKind.Absolute, out var dl)) continue;
                    Uri? forum = null;
                    if (!string.IsNullOrWhiteSpace(b.Forum) && Uri.TryCreate(b.Forum, UriKind.Absolute, out var fo)) forum = fo;

                    entries.Add(new RomEntry
                    {
                        Source = RomSource.CrDroid,
                        Kind = b.BuildType?.ToLowerInvariant() switch
                        {
                            "monthly" => RomKind.Monthly,
                            "weekly" => RomKind.Weekly,
                            "nightly" => RomKind.Nightly,
                            "alpha" => RomKind.Alpha,
                            "beta" => RomKind.Beta,
                            "stable" => RomKind.Stable,
                            _ => RomKind.Unknown,
                        },
                        Codename = slug,
                        Version = string.IsNullOrWhiteSpace(b.Version) ? branch : $"crDroid {b.Version} (Android {branch.Split('.')[0]})",
                        BuildDate = b.Timestamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(b.Timestamp) : default,
                        SizeBytes = b.Size,
                        FileName = b.Filename,
                        DownloadUrl = dl,
                        Sha256 = b.Sha256,
                        Md5 = b.Md5,
                        Maintainer = b.Maintainer,
                        ForumUrl = forum,
                    });
                }
            }
            catch (HttpRequestException) { }
            catch (JsonException) { }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
        }

        entries.Sort((a, b) => b.BuildDate.CompareTo(a.BuildDate));
        return entries;
    }

    private static readonly JsonSerializerOptions CrDroidJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record CrDroidResponse([property: JsonPropertyName("response")] List<CrDroidBuild>? Response);

    private sealed record CrDroidBuild(
        [property: JsonPropertyName("filename")] string? Filename,
        [property: JsonPropertyName("download")] string? Download,
        [property: JsonPropertyName("md5")] string? Md5,
        [property: JsonPropertyName("sha256")] string? Sha256,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("buildtype")] string? BuildType,
        [property: JsonPropertyName("timestamp")] long Timestamp,
        [property: JsonPropertyName("maintainer")] string? Maintainer,
        [property: JsonPropertyName("forum")] string? Forum
    );

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
