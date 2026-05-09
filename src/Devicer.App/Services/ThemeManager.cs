using System.Windows;

namespace Devicer.App.Services;

public sealed class ThemeManager
{
    private readonly AppSettingsStore _store;

    public event EventHandler<AppTheme>? ThemeChanged;

    public ThemeManager(AppSettingsStore store) => _store = store;

    public AppTheme Current => _store.Settings.Theme;

    public void Apply(AppTheme theme)
    {
        var dictName = theme switch
        {
            AppTheme.Latte => "CatppuccinLatte",
            _ => "CatppuccinMocha",
        };

        var newDict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Resources/Themes/{dictName}.xaml", UriKind.Absolute),
        };

        // Slot 0 is reserved for the palette dictionary; slot 1 is ThemeStyles. Replace slot 0 only.
        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = newDict;
        else merged.Add(newDict);

        _store.Settings.Theme = theme;
        _store.Save();

        ThemeChanged?.Invoke(this, theme);
    }
}
