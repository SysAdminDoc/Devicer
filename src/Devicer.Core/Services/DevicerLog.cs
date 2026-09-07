using System.IO;

namespace Devicer.Core.Services;

/// <summary>
/// Minimal append-only file logger. Writes to
/// <c>%LOCALAPPDATA%\Devicer\logs\devicer.log</c>. Thread-safe via a lock; one process
/// at a time. Used to capture FUS protocol traffic during firmware downloads so failures
/// can be diagnosed offline.
///
/// <para>Automatically rotates when the log passes <see cref="MaxBytes"/>; the previous
/// log is kept as <c>devicer.log.1</c>. Without rotation, repeated FUS attempts (which
/// dump full request/response bodies + every header) would grow the file without bound
/// and eventually fill <c>%LOCALAPPDATA%</c>.</para>
/// </summary>
public static class DevicerLog
{
    private const long MaxBytes = 4L * 1024 * 1024; // 4 MiB — small enough to read in any editor

    private static readonly object Sync = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Devicer", "logs", "devicer.log");

    private static string ArchivePath => LogPath + ".1";

    static DevicerLog()
    {
        var dir = Path.GetDirectoryName(LogPath)!;
        Directory.CreateDirectory(dir);
    }

    public static void Info(string source, string message) => Write("INFO ", source, message);
    public static void Warn(string source, string message) => Write("WARN ", source, message);
    public static void Error(string source, string message) => Write("ERROR", source, message);

    public static void Section(string heading)
    {
        lock (Sync)
        {
            try
            {
                RotateIfNeeded();
                File.AppendAllText(LogPath,
                    $"\n=== {DateTimeOffset.Now:HH:mm:ss} {heading} {new string('=', Math.Max(0, 60 - heading.Length))}\n");
            }
            catch { /* logger must never throw */ }
        }
    }

    public static void Write(string level, string source, string message)
    {
        var line = $"{DateTimeOffset.Now:HH:mm:ss.fff} {level} [{source}] {message}\n";
        lock (Sync)
        {
            try
            {
                RotateIfNeeded();
                File.AppendAllText(LogPath, line);
            }
            catch { /* logger must never throw */ }
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(LogPath)) return;
            var fi = new FileInfo(LogPath);
            if (fi.Length < MaxBytes) return;
            // Move current → .1 (overwriting any prior archive). Two-file ring keeps
            // ~8 MiB of history at most without ballooning forever.
            if (File.Exists(ArchivePath))
            {
                try { File.Delete(ArchivePath); } catch { /* best-effort */ }
            }
            File.Move(LogPath, ArchivePath);
        }
        catch { /* rotation failure must never block logging */ }
    }
}
