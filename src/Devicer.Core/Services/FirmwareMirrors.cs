namespace Devicer.Core.Services;

/// <summary>
/// One firmware-mirror site that hosts Samsung firmware via its own CDN, bypassing
/// Samsung's region geofence. See <c>docs/firmware-failover-research.md</c> for the
/// full inventory + verification dates.
/// </summary>
public sealed record FirmwareMirror(string Name, string UrlTemplate, string Note)
{
    /// <summary>Build a deep-link URL for the given model + region.</summary>
    public string BuildUrl(string model, string csc)
        => UrlTemplate
            .Replace("{model}", model.Trim().ToUpperInvariant())
            .Replace("{csc}", csc.Trim().ToUpperInvariant());
}

public static class FirmwareMirrors
{
    /// <summary>
    /// Public mirror sites that re-host Samsung firmware without region geofencing.
    /// Verified reachable as of 2026-05-09. URL templates use <c>{model}</c> and
    /// <c>{csc}</c> placeholders. Order roughly reflects reliability + catalog depth.
    /// </summary>
    public static readonly IReadOnlyList<FirmwareMirror> All = new[]
    {
        new FirmwareMirror(
            Name: "SamMobile",
            UrlTemplate: "https://www.sammobile.com/firmwares/database/{model}/{csc}/",
            Note: "Free tier requires login. Catalog is comprehensive."),
        new FirmwareMirror(
            Name: "SamFW",
            UrlTemplate: "https://samfw.com/firmware/{model}/{csc}",
            Note: "Cloudflare Turnstile challenge: solved automatically by your browser."),
        new FirmwareMirror(
            Name: "SamFrew",
            UrlTemplate: "https://samfrew.com/firmware/model/{model}/region/{csc}/upload/Desc/0/10",
            Note: "Listings public; downloads behind a free account."),
        new FirmwareMirror(
            Name: "SamFirms",
            UrlTemplate: "https://samfirms.com/?s={model}%20{csc}",
            Note: "Smaller catalog, no login required."),
    };
}
