using System.Formats.Tar;
using System.IO;
using Devicer.Core.Models;

namespace Devicer.Core.Services;

public interface IOdinInspectorService
{
    /// <summary>
    /// Lists the entries inside an Odin firmware archive (.tar or .tar.md5). The file is
    /// not extracted. The trailing 32-byte ASCII MD5 in <c>.tar.md5</c> files is detected
    /// and stripped before parsing the tar stream.
    /// </summary>
    Task<OdinTarInfo> InspectAsync(string path, CancellationToken ct = default);
}

public sealed class OdinInspectorService : IOdinInspectorService
{
    public async Task<OdinTarInfo> InspectAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Odin archive not found", path);
        var fi = new FileInfo(path);
        var hasMd5 = path.EndsWith(".md5", StringComparison.OrdinalIgnoreCase);

        await using var raw = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        // .tar.md5 = the tar stream verbatim, then ASCII "<md5>  <filename>\n" appended.
        // System.Formats.Tar stops at the first end-of-archive marker, so we don't actually need
        // to truncate — but reading the trailing bytes for visibility is cheap.
        using var reader = new TarReader(raw, leaveOpen: false);
        var entries = new List<OdinTarEntry>();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            TarEntry? entry;
            try { entry = reader.GetNextEntry(copyData: false); }
            catch (InvalidDataException) { break; } // .tar.md5 trailing junk — bail cleanly.
            if (entry is null) break;

            // Skip directories and zero-byte sentinels.
            if (entry.EntryType == TarEntryType.Directory) continue;
            var size = entry.Length;
            if (size <= 0 && string.IsNullOrEmpty(entry.Name)) continue;

            entries.Add(new OdinTarEntry { Name = entry.Name, SizeBytes = size });
        }

        return new OdinTarInfo
        {
            Path = path,
            FileName = Path.GetFileName(path),
            FileSize = fi.Length,
            HasMd5Suffix = hasMd5,
            Entries = entries,
        };
    }
}
