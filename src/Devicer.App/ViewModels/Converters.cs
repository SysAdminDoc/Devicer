using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Devicer.App.ViewModels;

public sealed class NotConverter : IValueConverter
{
    public static readonly NotConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    public static readonly NullToCollapsedConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null || (value is string s && string.IsNullOrWhiteSpace(s))
            ? Visibility.Collapsed
            : Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class NullToVisibleConverter : IValueConverter
{
    public static readonly NullToVisibleConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null || (value is string s && string.IsNullOrWhiteSpace(s))
            ? Visibility.Visible
            : Visibility.Collapsed;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class CountToCollapsedConverter : IValueConverter
{
    public static readonly CountToCollapsedConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i && i > 0 ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class CountToVisibleIfPositiveConverter : IValueConverter
{
    public static readonly CountToVisibleIfPositiveConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public static readonly InverseBoolToVisibilityConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Collapsed;
}

public sealed class KnoxIntactToVisibilityConverter : IValueConverter
{
    public static readonly KnoxIntactToVisibilityConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Samsung KG warranty bit: "0" = intact, "1" / "PRENORMAL"-/-other = tripped, null = unknown.
        var s = value as string;
        return s == "0" ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class KnoxTrippedToVisibilityConverter : IValueConverter
{
    public static readonly KnoxTrippedToVisibilityConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrWhiteSpace(s)) return Visibility.Collapsed;
        return s == "0" ? Visibility.Collapsed : Visibility.Visible;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
