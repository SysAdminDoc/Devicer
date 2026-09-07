using System.Text.RegularExpressions;

namespace Devicer.Core.Services;

/// <summary>
/// User-facing error classification: every known FUS failure mode translated to a Title +
/// plain-language Explanation + concrete SuggestedAction, with the raw protocol detail
/// preserved separately for advanced users.
/// </summary>
public sealed record FusFriendlyError(
    string Title,
    string Explanation,
    string SuggestedAction,
    string? TechnicalDetail = null,
    bool IsGeofence = false);

public static class FusErrorClassifier
{
    /// <summary>
    /// Classifies a <see cref="FusProtocolException"/> against the known failure patterns
    /// captured during Devicer's protocol reverse-engineering. Falls back to a generic
    /// "raw protocol error" for anything we haven't seen yet.
    /// </summary>
    /// <param name="ex">The exception thrown by FusClient / FirmwareDownloadService.</param>
    /// <param name="region">The CSC the caller asked for (e.g. <c>EUX</c>): surfaced
    /// in the geofence explanation so the user sees the region they're requesting vs.
    /// the IP region the CDN saw.</param>
    public static FusFriendlyError Classify(FusProtocolException ex, string? region = null)
    {
        var msg = ex.Message ?? string.Empty;
        var body = ex.ResponseBody ?? string.Empty;

        // ---- 403 / Squid ACL deny on the cloud-neofussvr edge ----
        // Samsung enforces geographic-origin filtering at the bulk-download CDN. The error
        // page conveniently includes the user's egress IP and the CDN node that handled
        // the request: we surface both so the user can see why it was denied.
        if (msg.Contains("HTTP 403") && body.Contains("Invalid Request") &&
            body.Contains("access control configuration"))
        {
            var ip = Regex.Match(body, @"IP:\s*([0-9a-fA-F\.:]+)").Groups[1].Value;
            var node = Regex.Match(body, @"Node information:\s*([A-Za-z0-9_\-]+)").Groups[1].Value;
            var nodeRegion = GuessNodeRegion(node);
            var regionPart = string.IsNullOrWhiteSpace(region) ? "" : $": your firmware region is {region.ToUpperInvariant()}";

            var explanation = string.Join('\n',
                $"Samsung's firmware-download CDN refused the request because it's coming from the wrong region{regionPart}.",
                "",
                $"  • Your egress IP    : {(string.IsNullOrEmpty(ip) ? "(not in response)" : ip)}",
                $"  • Samsung CDN node  : {(string.IsNullOrEmpty(node) ? "(unknown)" : $"{node}{(string.IsNullOrEmpty(nodeRegion) ? "" : $": {nodeRegion}")}")}",
                "",
                "Auth, IMEI, and protocol handshake all succeeded. This is purely a CDN geofence: " +
                "Samsung will not serve EU-region firmware (EUX, EUR, etc.) to a non-EU IP, won't serve " +
                "Korean firmware (KOO, OXM) to non-KR IPs, and so on. Every public FUS client " +
                "(samloader, SamFirm, Frija, Bifrost) hits the exact same wall.");

            var action = string.Join('\n',
                "1. Route the download through a VPN or proxy whose exit node matches your firmware's region.",
                "   For EUX firmware, a UK or Ireland VPN endpoint is reliable.",
                "2. Alternatively, run Devicer from a cloud VM hosted in the matching region",
                "   (AWS eu-west-1, Hetzner Falkenstein, etc.).",
                "3. As a last resort, request a CSC that matches your IP region: but this only helps if",
                "   your device can actually run that CSC's firmware (it usually can; CSC is software-side).");

            return new FusFriendlyError(
                Title: "Samsung CDN geographic restriction",
                Explanation: explanation,
                SuggestedAction: action,
                TechnicalDetail: $"Squid ACL deny on cloud-neofussvr.sslcs.cdngc.net. URL: {ExtractUrl(msg)}",
                IsGeofence: true);
        }

        // ---- 200 OK with empty body (we hit the wrong host) ----
        if (msg.Contains("HTTP 200") && body.Length == 0)
        {
            return new FusFriendlyError(
                Title: "Wrong FUS endpoint",
                Explanation: "Samsung accepted the download request but returned an empty body. " +
                             "This means we contacted the control-plane host (neofussvr) instead of the bulk-download host (cloud-neofussvr).",
                SuggestedAction: "Update Devicer: this is an internal routing bug, not a user issue.",
                TechnicalDetail: msg);
        }

        // ---- FUS Status 408 (auth failed: bad IMEI or wrong LOGIC_CHECK) ----
        var fusStatus = Regex.Match(body, @"<Status>(\d+)</Status>").Groups[1].Value;
        if (fusStatus == "408")
        {
            return new FusFriendlyError(
                Title: "Samsung FUS rejected authentication (Status 408)",
                Explanation: "The FUS server rejected the request signature, the IMEI, or the firmware version. " +
                             "Samsung deprecated the legacy fake-IMEI 00000000000000 in late 2024; a real 14-15 digit IMEI " +
                             "matching the model+region pair is required.",
                SuggestedAction: "Tap 'Open IMEI on phone' to launch Settings → About phone → Status, then copy your " +
                                 "actual IMEI 1 (15 digits) into the IMEI field. Verify the model and CSC are correct " +
                                 "(re-probe the device on the Device tab if you've moved SIMs).",
                TechnicalDetail: $"FUS Status 408: auth failed. Body: {Truncate(body, 400)}");
        }

        if (fusStatus == "5006")
        {
            return new FusFriendlyError(
                Title: "Region not authorized (FUS Status 5006)",
                Explanation: $"Samsung's FUS does not authorize firmware downloads for the region '{region ?? "?"}' " +
                             "with the supplied model+IMEI. This usually means the model isn't sold in that region, " +
                             "or the IMEI's home region differs from the requested CSC.",
                SuggestedAction: "Verify that the model+CSC pair is real (cross-check on samfw.com or sammobile.com). " +
                                 "If you've moved a phone between regions, the home CSC is locked to the original region.",
                TechnicalDetail: $"FUS Status 5006. Body: {Truncate(body, 400)}");
        }

        if (fusStatus == "5009")
        {
            return new FusFriendlyError(
                Title: "Invalid subscriber (FUS Status 5009)",
                Explanation: "Samsung's FUS thinks the IMEI is not a valid subscriber for the requested firmware. " +
                             "This typically means the IMEI you entered isn't valid, or it doesn't match a Samsung device that's known to that CSC.",
                SuggestedAction: "Re-check the IMEI (15 digits, no spaces). If it's correct, the device may have been " +
                                 "reported lost/stolen on Samsung's network: try a different IMEI from a known-good Samsung device.",
                TechnicalDetail: $"FUS Status 5009. Body: {Truncate(body, 400)}");
        }

        // ---- GenerateNonce returned no NONCE header (IP block / protocol change) ----
        if (msg.Contains("no NONCE header"))
        {
            return new FusFriendlyError(
                Title: "Samsung's FUS handshake didn't return a session nonce",
                Explanation: "GenerateNonce returned HTTP 200 but no NONCE response header. " +
                             "Most likely Samsung's edge is silently dropping our session because the IP is rate-limited, " +
                             "blocked, or because Samsung rotated the protocol.",
                SuggestedAction: "Wait a few minutes and retry: rate limits clear quickly. If it persists across IP " +
                                 "changes (try a VPN), Samsung may have rotated the wire protocol; check the Devicer roadmap for an update.",
                TechnicalDetail: msg);
        }

        // ---- Cryptographic padding error on nonce decrypt (Samsung rotated KEY_1) ----
        if (msg.Contains("Could not decrypt FUS nonce") || msg.Contains("Padding is invalid"))
        {
            return new FusFriendlyError(
                Title: "Cannot decrypt Samsung's session nonce",
                Explanation: "Devicer carries two known KEY_1 generations (current + legacy) and tries both: " +
                             "but the decoded result on each was non-printable. This means Samsung rotated the " +
                             "protocol key beyond what's known to the open-source FUS clients.",
                SuggestedAction: "Update Devicer when a new KEY_1 lands. samloader / SamloaderKotlin GitHub issues " +
                                 "are typically the first place the new key is published.",
                TechnicalDetail: msg);
        }

        // ---- Generic fallback ----
        return new FusFriendlyError(
            Title: "Samsung FUS protocol error",
            Explanation: "Samsung's firmware service returned an error Devicer doesn't have a dedicated explanation for yet.",
            SuggestedAction: "Check the technical detail below; the full request/response trace is in the log file at " +
                             $"%LOCALAPPDATA%\\Devicer\\logs\\devicer.log. If it reproduces, please report the issue.",
            TechnicalDetail: $"{msg}\n\n--- Server body ---\n{Truncate(body, 600)}");
    }

    /// <summary>
    /// Heuristic: Samsung CDN node names sometimes encode the city in three letters
    /// (BOS = Boston, FRA = Frankfurt, AMS = Amsterdam, NRT = Tokyo, …). We surface
    /// the city when we can recognize it so the user sees which edge served them.
    /// </summary>
    private static string? GuessNodeRegion(string node)
    {
        if (string.IsNullOrEmpty(node)) return null;
        var upper = node.ToUpperInvariant();
        foreach (var (code, region) in Cities)
            if (upper.Contains(code, StringComparison.Ordinal)) return region;
        return null;
    }

    private static readonly (string Code, string Region)[] Cities =
    {
        ("BOS", "Boston / US east"),
        ("LGA", "New York / US east"),
        ("DFW", "Dallas / US central"),
        ("LAX", "Los Angeles / US west"),
        ("SEA", "Seattle / US west"),
        ("ORD", "Chicago / US central"),
        ("ATL", "Atlanta / US east"),
        ("LHR", "London / UK"),
        ("FRA", "Frankfurt / Germany"),
        ("AMS", "Amsterdam / Netherlands"),
        ("CDG", "Paris / France"),
        ("MAD", "Madrid / Spain"),
        ("NRT", "Tokyo / Japan"),
        ("HND", "Tokyo / Japan"),
        ("ICN", "Seoul / South Korea"),
        ("SIN", "Singapore"),
        ("HKG", "Hong Kong"),
        ("SYD", "Sydney / Australia"),
        ("GRU", "São Paulo / Brazil"),
        ("YYZ", "Toronto / Canada"),
    };

    private static string? ExtractUrl(string message)
    {
        var m = Regex.Match(message, @"URL:\s*(\S+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty
        : s.Length <= max ? s
        : s[..max] + $"…[+{s.Length - max} more]";
}
