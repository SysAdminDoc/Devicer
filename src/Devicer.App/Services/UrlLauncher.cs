using System.Diagnostics;

namespace Devicer.App.Services;

/// <summary>
/// Centralized launcher for opening URLs the app didn't author itself — ROM-feed download
/// links, OEM portal URLs, etc. <c>Process.Start(new ProcessStartInfo { UseShellExecute =
/// true })</c> with a URL parameter that turns out to be <c>file:///...</c>,
/// <c>shell:...</c>, or <c>ms-settings:...</c> would happily invoke a local handler;
/// gating on http/https is an inexpensive defense-in-depth against a compromised JSON feed
/// or markdown-style data leaking into a button.
/// </summary>
public static class UrlLauncher
{
    /// <summary>
    /// Opens <paramref name="url"/> in the user's default browser if and only if it parses
    /// as a well-formed http/https Uri. Returns null on success or a short reason string
    /// on rejection / launch failure.
    /// </summary>
    public static string? TryOpen(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "URL is empty.";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "URL is not absolute or well-formed.";
        return TryOpen(uri);
    }

    /// <summary>Same as <see cref="TryOpen(string?)"/> but for an already-parsed Uri.</summary>
    public static string? TryOpen(Uri? uri)
    {
        if (uri is null) return "URL is null.";
        if (!uri.IsAbsoluteUri) return "URL is not absolute.";
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return $"Refusing to open non-http(s) URL ({uri.Scheme}://…).";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true,
            });
            return null;
        }
        catch (Exception ex)
        {
            return $"Could not open URL: {ex.Message}";
        }
    }
}
