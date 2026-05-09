using System.Reflection;

namespace Devicer.Core.Services;

/// <summary>
/// Single source of truth for the <c>User-Agent</c> version segment used by every HTTP
/// client in Devicer.Core (Samsung FUS, Samsung FOTA feed, LineageOS API, crDroid OTA
/// JSON). Reads from the assembly version so a release bump propagates automatically and
/// no individual UA string can drift again the way the prior hard-coded "Devicer/0.3" /
/// "Devicer/0.4" strings did.
/// </summary>
internal static class HttpUserAgent
{
    public static string AssemblyVersion { get; } =
        typeof(HttpUserAgent).Assembly.GetName().Version?.ToString(2) ?? "1.0";
}
