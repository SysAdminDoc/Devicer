using System.IO;

namespace Devicer.Core.Services;

public interface IPcPatchService
{
    bool IsAvailable { get; }

    Task<PatchResult> PatchBootImageAsync(
        string localBootImgPath,
        IProgress<PatchProgress>? progress,
        CancellationToken ct = default);
}

public sealed class PcPatchService : IPcPatchService
{
    private readonly IShellRunner _shell;
    private readonly IToolManager _tools;
    private readonly IHashService _hash;
    private readonly string _outRoot;

    public PcPatchService(IShellRunner shell, IToolManager tools, IHashService hash, string? outRoot = null)
    {
        _shell = shell;
        _tools = tools;
        _hash = hash;
        _outRoot = outRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Devicer", "patches", "pc-side");
    }

    public bool IsAvailable => _tools.Locate("magisk_patcher").IsAvailable;

    public async Task<PatchResult> PatchBootImageAsync(
        string localBootImgPath,
        IProgress<PatchProgress>? progress,
        CancellationToken ct = default)
    {
        var tool = _tools.Locate("magisk_patcher");
        if (!tool.IsAvailable || tool.Path is null)
            throw new InvalidOperationException("Magisk_patcher not found. Install affggh/Magisk_patcher or set its path in Settings.");

        if (!File.Exists(localBootImgPath))
            throw new FileNotFoundException("Boot image not found", localBootImgPath);

        DevicerLog.Section($"PC-side Magisk patch: {localBootImgPath}");
        progress?.Report(new PatchProgress(PatchPhase.Validating, "Validating boot image…"));

        var stamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd_HHmmss");
        var folder = Path.Combine(_outRoot, stamp);
        Directory.CreateDirectory(folder);

        var inputCopy = Path.Combine(folder, Path.GetFileName(localBootImgPath));
        File.Copy(localBootImgPath, inputCopy, overwrite: true);

        progress?.Report(new PatchProgress(PatchPhase.Patching, "Running Magisk_patcher on host…"));

        var r = await _shell.RunAsync(tool.Path, new[] { inputCopy }, TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);
        if (!r.Success)
        {
            DevicerLog.Error("PcPatch", $"magisk_patcher failed (exit {r.ExitCode}): {r.Stderr}");
            throw new InvalidOperationException($"Magisk_patcher failed (exit {r.ExitCode}): {Tail(r.Stdout + "\n" + r.Stderr, 600)}");
        }

        var outputPath = FindPatchedOutput(folder, inputCopy);
        if (outputPath is null)
            throw new InvalidOperationException("Could not locate patched output file. Check Magisk_patcher output.");

        progress?.Report(new PatchProgress(PatchPhase.Hashing, "Hashing patched image…"));
        var sha = await _hash.ComputeSha256Async(outputPath, ct).ConfigureAwait(false);
        var size = new FileInfo(outputPath).Length;

        progress?.Report(new PatchProgress(PatchPhase.Done, $"Done. Patched image at {outputPath}", 1.0));

        return new PatchResult(
            localBootImgPath,
            outputPath,
            Path.GetFileName(outputPath),
            sha,
            size,
            Models.RootKind.Magisk,
            null);
    }

    private static string? FindPatchedOutput(string folder, string inputPath)
    {
        var inputName = Path.GetFileName(inputPath);
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, inputName, StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Contains("patched", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("new-", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("magisk", StringComparison.OrdinalIgnoreCase))
                return file;
        }
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, inputName, StringComparison.OrdinalIgnoreCase)) continue;
            if (name.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
                return file;
        }
        return null;
    }

    private static string Tail(string s, int max) => s.Length <= max ? s : "..." + s[^max..];
}
