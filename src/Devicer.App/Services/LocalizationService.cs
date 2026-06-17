using System.Globalization;
using System.Resources;

namespace Devicer.App.Services;

public static class LocalizationService
{
    private static readonly ResourceManager _resources = new(
        "Devicer.App.Resources.Strings.Strings",
        typeof(LocalizationService).Assembly);

    public static string Get(string key)
        => _resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Get(string key, CultureInfo culture)
        => _resources.GetString(key, culture) ?? key;

    public static CultureInfo CurrentCulture
    {
        get => CultureInfo.CurrentUICulture;
        set
        {
            CultureInfo.CurrentUICulture = value;
            CultureInfo.CurrentCulture = value;
        }
    }
}
