using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Devicer.Core.Services;

/// <summary>
/// Local firmware cache rooted at <c>%LOCALAPPDATA%\Devicer\firmware\</c>.
/// Each entry lives in its own subfolder named <c>&lt;model&gt;_&lt;region&gt;_&lt;version-pda&gt;</c>
/// containing the encrypted blob, the decrypted firmware, and an <c>index.json</c> manifest.
/// </summary>
public sealed class FirmwareCache
{
    public sealed record IndexRecord(
        string Model,
        string Region,
        string Version,
        string BinaryName,
        long EncryptedSize,
        long DecryptedSize,
        string EncryptedSha256,
        DateTimeOffset CompletedUtc
    );

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _root;

    public FirmwareCache(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Devicer",
            "firmware");
    }

    public string Root => _root;

    /// <summary>Folder for a (model, region, version) triple. Created on first use.</summary>
    public string PrepareFolder(string model, string region, string version)
    {
        var pda = ExtractPda(version);
        var slug = SafeSlug($"{model}_{region}_{pda}");
        var path = Path.Combine(_root, slug);
        Directory.CreateDirectory(path);
        return path;
    }

    public void WriteIndex(string folder, IndexRecord record)
    {
        var path = Path.Combine(folder, "index.json");
        File.WriteAllText(path, JsonSerializer.Serialize(record, JsonOpts));
    }

    public IndexRecord? ReadIndex(string folder)
    {
        var path = Path.Combine(folder, "index.json");
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<IndexRecord>(File.ReadAllText(path), JsonOpts); }
        catch { return null; }
    }

    /// <summary>Lists every cached firmware entry on disk (skips folders without an index).</summary>
    public IEnumerable<(string Folder, IndexRecord Record)> Enumerate()
    {
        if (!Directory.Exists(_root)) yield break;
        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            var rec = ReadIndex(dir);
            if (rec is not null) yield return (dir, rec);
        }
    }

    private static string ExtractPda(string version)
    {
        // The version may be "PDA/CSC/CP[/BOOT]" — fall back to the whole string if no slash.
        var slash = version.IndexOf('/');
        return slash > 0 ? version[..slash] : version;
    }

    private static string SafeSlug(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = input.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
