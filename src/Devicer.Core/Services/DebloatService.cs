using System.Text.RegularExpressions;

namespace Devicer.Core.Services;

public enum PackageSafety
{
    Safe,
    Advanced,
    Expert,
    Unsafe,
    Unknown,
}

public sealed record InstalledPackage(
    string PackageName,
    bool IsEnabled,
    PackageSafety Safety,
    string? Description
);

public interface IDebloatService
{
    Task<IReadOnlyList<InstalledPackage>> ListPackagesAsync(string serial, CancellationToken ct = default);
    Task<bool> DisablePackageAsync(string serial, string packageName, CancellationToken ct = default);
    Task<bool> EnablePackageAsync(string serial, string packageName, CancellationToken ct = default);
    Task<bool> UninstallPackageAsync(string serial, string packageName, CancellationToken ct = default);
}

public sealed class DebloatService : IDebloatService
{
    private readonly IAdbService _adb;

    public DebloatService(IAdbService adb) => _adb = adb;

    public async Task<IReadOnlyList<InstalledPackage>> ListPackagesAsync(string serial, CancellationToken ct = default)
    {
        var enabledResult = await _adb.RunShellAsync(serial, "pm list packages -e", TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        var disabledResult = await _adb.RunShellAsync(serial, "pm list packages -d", TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

        var enabled = ParsePackageList(enabledResult.Stdout);
        var disabled = ParsePackageList(disabledResult.Stdout);

        var packages = new List<InstalledPackage>();
        foreach (var pkg in enabled)
            packages.Add(new InstalledPackage(pkg, IsEnabled: true, ClassifyPackage(pkg), DescribePackage(pkg)));
        foreach (var pkg in disabled)
            packages.Add(new InstalledPackage(pkg, IsEnabled: false, ClassifyPackage(pkg), DescribePackage(pkg)));

        return packages.OrderBy(p => p.Safety).ThenBy(p => p.PackageName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<bool> DisablePackageAsync(string serial, string packageName, CancellationToken ct = default)
    {
        DevicerLog.Info("Debloat", $"Disabling {packageName}");
        var r = await _adb.RunShellAsync(serial, $"pm disable-user --user 0 {packageName}", TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        return r.Success;
    }

    public async Task<bool> EnablePackageAsync(string serial, string packageName, CancellationToken ct = default)
    {
        DevicerLog.Info("Debloat", $"Enabling {packageName}");
        var r = await _adb.RunShellAsync(serial, $"pm enable {packageName}", TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        return r.Success;
    }

    public async Task<bool> UninstallPackageAsync(string serial, string packageName, CancellationToken ct = default)
    {
        DevicerLog.Info("Debloat", $"Uninstalling {packageName} for user 0");
        var r = await _adb.RunShellAsync(serial, $"pm uninstall -k --user 0 {packageName}", TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        return r.Success;
    }

    private static List<string> ParsePackageList(string output)
    {
        var list = new List<string>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("package:", StringComparison.Ordinal))
                list.Add(trimmed[8..]);
        }
        return list;
    }

    private static PackageSafety ClassifyPackage(string pkg)
    {
        var lower = pkg.ToLowerInvariant();
        if (UnsafePackages.Any(p => lower.Contains(p))) return PackageSafety.Unsafe;
        if (ExpertPackages.Any(p => lower.Contains(p))) return PackageSafety.Expert;
        if (AdvancedPackages.Any(p => lower.Contains(p))) return PackageSafety.Advanced;
        if (SafePackages.Any(p => lower.Contains(p))) return PackageSafety.Safe;
        return PackageSafety.Unknown;
    }

    private static string? DescribePackage(string pkg)
    {
        var lower = pkg.ToLowerInvariant();
        foreach (var (pattern, desc) in KnownDescriptions)
            if (lower.Contains(pattern)) return desc;
        return null;
    }

    private static readonly string[] SafePackages =
    [
        "facebook", "flipboard", "linkedin", "tiktok", "spotify",
        "bixby", "samsungpay.gear", "game.service", "samsung.aremoji",
        "ar.emoji", "samsung.storyservice", "svoice", "samsung.visionintelligence",
    ];

    private static readonly string[] AdvancedPackages =
    [
        "weather", "onedrive", "office.mobile", "skype",
        "samsung.android.calendar", "samsung.android.email",
        "samsung.android.app.tips", "samsung.android.forest",
    ];

    private static readonly string[] ExpertPackages =
    [
        "samsung.android.voc", "samsung.android.app.watchmanagerstub",
        "google.android.apps.turbo", "google.android.feedback",
    ];

    private static readonly string[] UnsafePackages =
    [
        "android.providers.contacts", "android.providers.media",
        "android.systemui", "android.settings", "android.phone",
        "android.providers.telephony", "android.bluetooth",
    ];

    private static readonly (string Pattern, string Description)[] KnownDescriptions =
    [
        ("facebook.katana", "Facebook app"),
        ("facebook.services", "Facebook background services"),
        ("facebook.system", "Facebook system integration"),
        ("facebook.appmanager", "Facebook app manager"),
        ("flipboard", "Flipboard news aggregator"),
        ("linkedin", "LinkedIn"),
        ("bixby", "Samsung Bixby voice assistant"),
        ("samsungpay", "Samsung Pay"),
        ("samsung.aremoji", "Samsung AR Emoji"),
        ("game.service", "Samsung Game Launcher"),
        ("onedrive", "Microsoft OneDrive"),
        ("office.mobile", "Microsoft Office Mobile"),
        ("skype", "Microsoft Skype"),
        ("spotify", "Spotify (pre-installed)"),
        ("samsung.android.calendar", "Samsung Calendar"),
        ("samsung.android.email", "Samsung Email"),
        ("samsung.android.app.tips", "Samsung Tips"),
        ("google.android.apps.turbo", "Google Device Health Services"),
    ];
}
