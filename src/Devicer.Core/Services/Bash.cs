namespace Devicer.Core.Services;

/// <summary>
/// Minimal shell-quoting helpers for adb-shell command construction. Android's <c>sh</c>
/// follows POSIX quoting rules — single-quoting blocks all expansion except the literal
/// single-quote itself, which we escape via the standard <c>'\''</c> trick.
/// </summary>
internal static class Bash
{
    public static string Quote(string s)
    {
        if (string.IsNullOrEmpty(s)) return "''";
        return "'" + s.Replace("'", "'\\''") + "'";
    }
}
