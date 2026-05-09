using System.IO;
using System.Security.Cryptography;
using Devicer.Core.Models;

namespace Devicer.Core.Services;

public enum PatchPhase
{
    Validating,
    Pushing,
    Patching,
    Pulling,
    Hashing,
    Done,
    Cancelled,
    Failed,
}

public sealed record PatchProgress(PatchPhase Phase, string Message, double? Fraction = null);

public sealed record PatchResult(
    string InputPath,
    string OutputPath,
    string OutputFileName,
    string Sha256,
    long SizeBytes,
    RootKind PatchedBy,
    string? Version);

public interface IBootPatchService
{
    /// <summary>
    /// Patches a boot.img / init_boot.img on the device's root manager (Magisk / KernelSU /
    /// APatch). The image is pushed to <c>/data/local/tmp</c>, patched on-device, then the
    /// resulting <c>new-boot.img</c> is pulled to the host and SHA256-recorded.
    /// </summary>
    Task<PatchResult> PatchBootImageAsync(
        string serial,
        RootStatus rootStatus,
        string localBootImgPath,
        IProgress<PatchProgress>? progress,
        CancellationToken ct = default);
}

public sealed class BootPatchService : IBootPatchService
{
    private readonly IAdbService _adb;
    private readonly string _outRoot;

    public BootPatchService(IAdbService adb, string? outRoot = null)
    {
        _adb = adb;
        _outRoot = outRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Devicer", "patches");
    }

    public string Root => _outRoot;

    public async Task<PatchResult> PatchBootImageAsync(
        string serial,
        RootStatus rootStatus,
        string localBootImgPath,
        IProgress<PatchProgress>? progress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serial)) throw new ArgumentException("serial required", nameof(serial));
        if (!File.Exists(localBootImgPath)) throw new FileNotFoundException("Boot image not found", localBootImgPath);
        if (rootStatus.Kind == RootKind.None) throw new InvalidOperationException("Patching requires a rooted device. Detected: None.");

        progress?.Report(new PatchProgress(PatchPhase.Validating, $"Detected {rootStatus.Kind} {rootStatus.Version}"));

        var stamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd_HHmmss");
        var folder = Path.Combine(_outRoot, SafeSlug(serial), stamp);
        Directory.CreateDirectory(folder);

        var sourceName = Path.GetFileName(localBootImgPath);
        var remoteSrc = $"/data/local/tmp/{sourceName}";

        try
        {
            progress?.Report(new PatchProgress(PatchPhase.Pushing, $"Pushing {sourceName} to /data/local/tmp …"));
            var pushRes = await _adb.RunShellAsync(serial, $"rm -f {Bash.Quote(remoteSrc)}", TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            // Use adb push (not shell) for binary safety.
            var push = await PushAsync(serial, localBootImgPath, remoteSrc, ct).ConfigureAwait(false);
            if (!push.Success)
                throw new InvalidOperationException($"adb push failed: {push.Stderr.Trim()}");

            string remoteOutput;
            string outputFileName;
            switch (rootStatus.Kind)
            {
                case RootKind.Magisk:
                    (remoteOutput, outputFileName) = await PatchWithMagiskAsync(serial, remoteSrc, sourceName, progress, ct).ConfigureAwait(false);
                    break;
                case RootKind.KernelSU:
                    (remoteOutput, outputFileName) = await PatchWithKernelSuAsync(serial, remoteSrc, progress, ct).ConfigureAwait(false);
                    break;
                case RootKind.APatch:
                    (remoteOutput, outputFileName) = await PatchWithAPatchAsync(serial, remoteSrc, progress, ct).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported root manager '{rootStatus.Kind}'. Magisk / KernelSU / APatch only.");
            }

            progress?.Report(new PatchProgress(PatchPhase.Pulling, $"Pulling {Path.GetFileName(remoteOutput)} to host …"));
            var localOut = Path.Combine(folder, outputFileName);
            var pull = await _adb.PullAsync(serial, remoteOutput, localOut, TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);
            if (!pull.Success || !File.Exists(localOut))
                throw new InvalidOperationException($"adb pull failed: {pull.Stderr.Trim()}");

            progress?.Report(new PatchProgress(PatchPhase.Hashing, "Hashing patched image…"));
            var sha = await ComputeSha256Async(localOut, ct).ConfigureAwait(false);
            var size = new FileInfo(localOut).Length;

            // Cleanup remote artifacts (non-fatal).
            try
            {
                await _adb.RunShellAsync(serial, $"rm -f {Bash.Quote(remoteSrc)}", TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                await _adb.RunSuAsync(serial, $"rm -f {Bash.Quote(remoteOutput)}", TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            }
            catch { /* best-effort */ }

            progress?.Report(new PatchProgress(PatchPhase.Done, $"Done. Patched image at {localOut}", 1.0));
            return new PatchResult(localBootImgPath, localOut, outputFileName, sha, size, rootStatus.Kind, rootStatus.Version);
        }
        catch (OperationCanceledException)
        {
            progress?.Report(new PatchProgress(PatchPhase.Cancelled, "Cancelled."));
            try { await _adb.RunShellAsync(serial, $"rm -f {Bash.Quote(remoteSrc)}", TimeSpan.FromSeconds(5), ct).ConfigureAwait(false); }
            catch { }
            throw;
        }
    }

    private async Task<(string remoteOutput, string outputFileName)> PatchWithMagiskAsync(string serial, string remoteSrc, string sourceName, IProgress<PatchProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new PatchProgress(PatchPhase.Patching, "Running Magisk boot_patch.sh on-device…"));

        // Magisk's installer ships boot_patch.sh + magiskboot + magiskinit at /data/adb/magisk.
        // We KEEPVERITY/KEEPFORCEENCRYPT=true to preserve dm-verity on devices that need it (default Magisk behavior).
        var script =
            "set -e; " +
            "cp -f " + Bash.Quote(remoteSrc) + " /data/adb/magisk/boot.img; " +
            "cd /data/adb/magisk; " +
            "KEEPVERITY=true KEEPFORCEENCRYPT=true sh boot_patch.sh boot.img 2>&1; " +
            "echo --PATCH-OK--; " +
            "ls -la new-boot.img";

        var result = await _adb.RunSuAsync(serial, script, TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);
        if (!result.Stdout.Contains("--PATCH-OK--"))
        {
            throw new InvalidOperationException(
                $"Magisk boot_patch.sh failed (exit {result.ExitCode}). stdout-tail: {Tail(result.Stdout, 600)} stderr: {Tail(result.Stderr, 400)}");
        }

        var stem = Path.GetFileNameWithoutExtension(sourceName);
        return ("/data/adb/magisk/new-boot.img", $"{stem}-magisk-patched.img");
    }

    private async Task<(string remoteOutput, string outputFileName)> PatchWithKernelSuAsync(string serial, string remoteSrc, IProgress<PatchProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new PatchProgress(PatchPhase.Patching, "Running ksud boot-patch on-device…"));
        // ksud writes the patched image next to the input.
        var workDir = "/data/local/tmp";
        var script = $"cd {workDir} && ksud boot-patch -b {Bash.Quote(Path.GetFileName(remoteSrc))} 2>&1 && ls -la kernelsu_patched_*.img";
        var result = await _adb.RunSuAsync(serial, script, TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException($"ksud boot-patch failed (exit {result.ExitCode}). {Tail(result.Stdout + result.Stderr, 800)}");

        var patched = ParseFirstFilename(result.Stdout, "kernelsu_patched_");
        if (patched is null) throw new InvalidOperationException("Could not locate the kernelsu_patched_*.img output. Stdout: " + Tail(result.Stdout, 600));
        return ($"{workDir}/{patched}", patched);
    }

    private async Task<(string remoteOutput, string outputFileName)> PatchWithAPatchAsync(string serial, string remoteSrc, IProgress<PatchProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new PatchProgress(PatchPhase.Patching, "Running apd patch on-device…"));
        var workDir = "/data/local/tmp";
        var script = $"cd {workDir} && apd patch -b {Bash.Quote(Path.GetFileName(remoteSrc))} 2>&1 && ls -la apatch_patched_*.img";
        var result = await _adb.RunSuAsync(serial, script, TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException($"apd patch failed (exit {result.ExitCode}). {Tail(result.Stdout + result.Stderr, 800)}");

        var patched = ParseFirstFilename(result.Stdout, "apatch_patched_");
        if (patched is null) throw new InvalidOperationException("Could not locate the apatch_patched_*.img output. Stdout: " + Tail(result.Stdout, 600));
        return ($"{workDir}/{patched}", patched);
    }

    private async Task<ShellResult> PushAsync(string serial, string localPath, string remotePath, CancellationToken ct)
    {
        // We don't have a typed Push helper, so route through the IShellRunner-like adb call directly.
        // Use AdbService.PullAsync's underlying shell would be wrong direction; reuse RunShellAsync isn't right either.
        // Instead, run adb push as a one-off process via ProcessStartInfo.
        var psi = new System.Diagnostics.ProcessStartInfo("adb")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-s"); psi.ArgumentList.Add(serial);
        psi.ArgumentList.Add("push"); psi.ArgumentList.Add(localPath); psi.ArgumentList.Add(remotePath);

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start adb.");
        var so = proc.StandardOutput.ReadToEndAsync(ct);
        var se = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return new ShellResult(proc.ExitCode, await so.ConfigureAwait(false), await se.ConfigureAwait(false));
    }

    private static string? ParseFirstFilename(string output, string prefix)
    {
        foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = raw.IndexOf(prefix, StringComparison.Ordinal);
            if (idx < 0) continue;
            var rest = raw[idx..];
            // Trim at first whitespace.
            for (int i = 0; i < rest.Length; i++)
            {
                if (char.IsWhiteSpace(rest[i])) return rest[..i];
            }
            return rest;
        }
        return null;
    }

    private static string Tail(string s, int max) => s.Length <= max ? s : "…" + s[^max..];

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        using var sha = SHA256.Create();
        var buf = new byte[1 << 16];
        int n;
        while ((n = await fs.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
            sha.TransformBlock(buf, 0, n, null, 0);
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    private static string SafeSlug(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = input.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
