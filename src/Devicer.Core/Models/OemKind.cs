namespace Devicer.Core.Models;

public enum OemKind
{
    Unknown,
    Samsung,
    Google,
    OnePlus,
    Xiaomi,
    Sony,
    Asus,
    Motorola,
    Nothing,
    Realme,
    Oppo,
    Vivo,
    Other,
}

public static class OemKindExtensions
{
    public static OemKind Detect(string? manufacturer, string? brand)
    {
        var v = (manufacturer ?? brand ?? string.Empty).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(v)) return OemKind.Unknown;
        return v switch
        {
            _ when v.Contains("samsung") => OemKind.Samsung,
            _ when v.Contains("google") => OemKind.Google,
            _ when v.Contains("oneplus") => OemKind.OnePlus,
            _ when v.Contains("xiaomi") || v.Contains("redmi") || v.Contains("poco") || v.Contains("mi ") => OemKind.Xiaomi,
            _ when v.Contains("sony") => OemKind.Sony,
            _ when v.Contains("asus") => OemKind.Asus,
            _ when v.Contains("motorola") || v.Contains("lenovo") => OemKind.Motorola,
            _ when v.Contains("nothing") => OemKind.Nothing,
            _ when v.Contains("realme") => OemKind.Realme,
            _ when v.Contains("oppo") => OemKind.Oppo,
            _ when v.Contains("vivo") => OemKind.Vivo,
            _ => OemKind.Other,
        };
    }

    public static string DisplayName(this OemKind kind) => kind switch
    {
        OemKind.OnePlus => "OnePlus",
        OemKind.Unknown => "(unknown)",
        _ => kind.ToString(),
    };
}
