using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Devicer.Core.Services;

/// <summary>
/// FUS-protocol HTTP session.
///
/// • Maintains the rotating server NONCE + JSESSIONID across requests.
/// • Builds the <c>Authorization: FUS …</c> header for each call.
/// • Parses any new <c>NONCE</c> response header and rotates internally.
///
/// Endpoints:
///   POST https://neofussvr.sslcs.cdngc.net/&lt;op&gt;             — control plane (XML in/out)
///   GET  http://cloud-neofussvr.sslcs.cdngc.net/NF_DownloadBinaryForMass.do — bulk download (HTTP, range supported)
/// </summary>
public sealed class FusClient : IDisposable
{
    public const string ApiHost = "https://neofussvr.sslcs.cdngc.net";
    // Samsung's CDN now refuses plaintext HTTP on the cloud bulk-download host (returns
    // a Squid 403 "access control configuration prevents your request"). Use HTTPS.
    public const string CloudHost = "https://cloud-neofussvr.sslcs.cdngc.net";

    private const string GenerateNoncePath = "/NF_DownloadGenerateNonce.do";
    private const string DownloadPath = "/NF_DownloadBinaryForMass.do";
    private const string UserAgent = "Kies2.0_FUS";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly CookieContainer _cookies;

    private string? _decodedNonce;     // 16-char ASCII session nonce (decrypted from NONCE response)
    private string? _encryptedNonce;   // raw Base64 string the server gave us; echoed back verbatim
    private string _key1Used = FusCrypto.Key1Current;  // generation that successfully decoded the current nonce

    public FusClient(HttpClient? http = null)
    {
        if (http is null)
        {
            _cookies = new CookieContainer();
            var handler = new HttpClientHandler
            {
                // The cloud-neofussvr download endpoint's Squid ACL requires the session
                // cookies (JSESSIONID_SVR + SCOUTER + Imperva tracking pair) set during the
                // POST handshake. Without them: HTTP 403 "Invalid Request". Imperva's
                // bot-detection turned out to be a non-issue on the API path.
                CookieContainer = _cookies,
                UseCookies = true,
                AllowAutoRedirect = false,
            };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
            _http.DefaultRequestHeaders.UserAgent.Clear();
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
            _ownsHttp = true;
        }
        else
        {
            _http = http;
            _cookies = new CookieContainer();
            _ownsHttp = false;
        }
    }

    /// <summary>
    /// The currently-known decoded nonce. Null until the first call to <see cref="EnsureSessionAsync"/>.
    /// </summary>
    public string? Nonce => _decodedNonce;

    /// <summary>
    /// Establishes a session by hitting the GenerateNonce endpoint. Required before any
    /// authenticated request. Idempotent — re-call to refresh.
    /// </summary>
    public async Task EnsureSessionAsync(CancellationToken ct = default)
    {
        if (_decodedNonce is not null) return;
        await RotateNonceAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// POSTs an XML request body to a FUS control-plane endpoint and returns the raw
    /// response body. Adds the FUS Authorization header, handles nonce rotation if the
    /// server sends a fresh NONCE header.
    /// </summary>
    public async Task<string> PostXmlAsync(string path, string xmlBody, CancellationToken ct = default)
    {
        await EnsureSessionAsync(ct).ConfigureAwait(false);

        // Samsung's FUS parser is strict about Content-Type: it wants "application/xml" with
        // no charset suffix. .NET's StringContent ctor auto-appends "; charset=utf-8" — strip it.
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(xmlBody));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/xml");
        using var req = new HttpRequestMessage(HttpMethod.Post, ApiHost + path) { Content = content };
        AddAuthHeader(req);

        DevicerLog.Info("FUS", $"POST {path} (body {xmlBody.Length} bytes)");
        DevicerLog.Info("FUS", $"  request body: {Truncate(xmlBody, 500)}");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        ConsumeRotatedNonce(resp);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        DevicerLog.Info("FUS", $"  response: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}, body {body.Length} bytes");
        DevicerLog.Info("FUS", $"  response body: {Truncate(body, 800)}");
        foreach (var h in resp.Headers.NonValidated.Concat(resp.Content.Headers.NonValidated))
            DevicerLog.Info("FUS", $"    hdr {h.Key}: {string.Join(" | ", h.Value)}");

        if (!resp.IsSuccessStatusCode)
        {
            DevicerLog.Error("FUS", $"POST {path} failed: HTTP {(int)resp.StatusCode}");
            throw new FusProtocolException($"FUS POST {path} failed: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}", body);
        }
        return body;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty
        : s.Length <= max ? s
        : s[..max] + $"…[+{s.Length - max} more]";

    /// <summary>
    /// Streams an encrypted firmware blob from the bulk-download endpoint.
    /// Caller is responsible for piping the returned stream to disk and decrypting.
    /// Range header allows resume; pass null for a fresh download.
    /// </summary>
    public async Task<HttpResponseMessage> StartDownloadAsync(string remoteFileName, long? rangeFrom, CancellationToken ct = default)
    {
        await EnsureSessionAsync(ct).ConfigureAwait(false);
        // Samsung's CDN expects the file= value with literal slashes (e.g. /neofus/9/SW_...enc4).
        // Uri.EscapeDataString would percent-encode the slashes (%2F) which Samsung rejects with
        // HTTP 403. Only escape characters that genuinely need encoding (space, &, =, ?, #).
        var encoded = EscapeFileQueryValue(remoteFileName);

        // The actual blob is only served by the cloud-neofussvr edge. The API host accepts
        // the GET but returns Content-Length: 0 (control-plane only). cloud-neofussvr's Squid
        // ACL also needs the JSESSIONID_SVR + Imperva session cookies that get set during the
        // POST handshake — without UseCookies=true, the CDN denies with "Invalid Request".
        var url = $"{CloudHost}{DownloadPath}?file={encoded}";
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        // Force HTTP/1.1 — Samsung's cloud-neofussvr Squid edge rejects HTTP/2 with a generic
        // "Invalid Request" ACL deny. Smart Switch (the reference client) is built on WinHTTP,
        // which only speaks HTTP/1.1, so the edge config is calibrated for that.
        req.Version = System.Net.HttpVersion.Version11;
        req.VersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionExact;
        AddAuthHeader(req);
        // Always send Range — Samsung's bulk CDN requires it on every download request.
        // Without it, some edge nodes return a Squid ACL-deny 403.
        req.Headers.Range = new RangeHeaderValue(rangeFrom ?? 0L, null);
        // Belt-and-suspenders: explicit UA on the request itself (in case the default doesn't propagate).
        req.Headers.UserAgent.Clear();
        req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

        DevicerLog.Info("FUS", $"GET download URL: {url}");
        DevicerLog.Info("FUS", $"  Range: bytes={rangeFrom ?? 0}-");
        DevicerLog.Info("FUS", $"  remoteFileName arg: '{remoteFileName}' (len {remoteFileName.Length})");
        // Dump every outgoing request header so we can spot what Smart Switch sends that we don't.
        foreach (var h in req.Headers)
            DevicerLog.Info("FUS", $"  REQ hdr {h.Key}: {string.Join(" | ", h.Value)}");

        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        DevicerLog.Info("FUS", $"  response: HTTP/{resp.Version} {(int)resp.StatusCode} {resp.ReasonPhrase}");
        DevicerLog.Info("FUS", $"  Content-Length: {resp.Content.Headers.ContentLength}, Content-Type: {resp.Content.Headers.ContentType}");

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            // Log full body for 403/4xx — Samsung's CDN often embeds the actual block reason here.
            DevicerLog.Error("FUS", $"  full body ({body.Length} bytes):\n{body}");
            DevicerLog.Error("FUS", $"  response headers:");
            foreach (var h in resp.Headers.NonValidated.Concat(resp.Content.Headers.NonValidated))
                DevicerLog.Error("FUS", $"    {h.Key}: {string.Join(" | ", h.Value)}");
            resp.Dispose();
            throw new FusProtocolException($"FUS download failed: HTTP {(int)resp.StatusCode} — URL: {url}", body);
        }
        return resp;
    }

    /// <summary>
    /// Conservative URL-query-value escape that preserves literal slashes. Samsung's FUS
    /// CDN treats <c>/</c> as part of the file path, not a separator, so we must NOT
    /// percent-escape it.
    /// </summary>
    private static string EscapeFileQueryValue(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                || c == '-' || c == '_' || c == '.' || c == '~' || c == '/')
            {
                sb.Append(c);
            }
            else
            {
                foreach (var b in System.Text.Encoding.UTF8.GetBytes(new[] { c }))
                    sb.Append('%').Append(b.ToString("X2"));
            }
        }
        return sb.ToString();
    }

    private static string? ExtractNonce(HttpResponseMessage resp)
    {
        // Samsung's NONCE header value contains base64 — including '/' and '+' — which
        // .NET's HttpClient parser refuses to validate, leaving the header in the
        // NonValidated collection. NonValidated.TryGetValues only matches *known* header
        // descriptors so it returns false for "NONCE"; iteration works.
        foreach (var kvp in resp.Headers.NonValidated)
        {
            if (!string.Equals(kvp.Key, "NONCE", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var s in kvp.Value)
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
        }
        foreach (var kvp in resp.Content.Headers.NonValidated)
        {
            if (!string.Equals(kvp.Key, "NONCE", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var s in kvp.Value)
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
        }
        return null;
    }

    private async Task RotateNonceAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, ApiHost + GenerateNoncePath);
        // Empty body, but the FUS Authorization header with empty signature/nonce is required.
        req.Headers.TryAddWithoutValidation("Authorization",
            "FUS nonce=\"\", signature=\"\", nc=\"\", type=\"\", realm=\"\", newauth=\"1\"");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        ConsumeRotatedNonce(resp);
        if (_decodedNonce is null)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new FusProtocolException(
                $"FUS GenerateNonce returned no NONCE header (HTTP {(int)resp.StatusCode}). Samsung may be blocking the source IP, or the protocol changed.",
                body);
        }
    }

    private void ConsumeRotatedNonce(HttpResponseMessage resp)
    {
        // Samsung's NONCE header is base64 — some characters trip .NET's validating header
        // parsers, which moves the header into HeaderValuesCollection.NonValidated instead
        // of the parsed Headers/Content.Headers collections. Read NonValidated first.
        string? enc = ExtractNonce(resp);
        if (string.IsNullOrWhiteSpace(enc)) return;

        // Decode best-effort: Samsung's NONCE header sometimes returns short / non-padded
        // values that aren't decryptable. The previous nonce remains valid for further
        // requests, so a rotation failure is non-fatal — it just means we'll keep using
        // the current session nonce until the server forces a re-handshake.
        try
        {
            var decoded = FusCrypto.DecryptNonceWithKey(enc);
            _encryptedNonce = enc;
            _decodedNonce = decoded.Nonce;
            _key1Used = decoded.Key1Used;
        }
        catch
        {
            // Existing nonce remains valid; let the next call surface any auth failure.
        }
    }

    private void AddAuthHeader(HttpRequestMessage req)
    {
        if (_decodedNonce is null || _encryptedNonce is null)
            throw new InvalidOperationException("FusClient: no session — call EnsureSessionAsync first.");
        var sig = FusCrypto.ComputeAuthSignature(_decodedNonce, _key1Used);
        var auth = $"FUS nonce=\"{_encryptedNonce}\", signature=\"{sig}\", nc=\"\", type=\"\", realm=\"\", newauth=\"1\"";
        req.Headers.TryAddWithoutValidation("Authorization", auth);
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}

public sealed class FusProtocolException : Exception
{
    public string? ResponseBody { get; }
    public FusProtocolException(string message, string? body = null) : base(message) { ResponseBody = body; }
    public FusProtocolException(string message, Exception inner) : base(message, inner) { }
}
