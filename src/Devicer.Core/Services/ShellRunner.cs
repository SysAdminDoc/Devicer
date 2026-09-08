using System.Diagnostics;
using System.Text;

namespace Devicer.Core.Services;

public sealed class ShellRunner(bool allowProcesses = true) : IShellRunner
{
    public async Task<ShellResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (!allowProcesses)
            throw new InvalidOperationException("External tool commands are disabled in sample capture mode.");
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start process: {fileName}");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var combined = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout is { } t) combined.CancelAfter(t);

        try
        {
            await process.WaitForExitAsync(combined.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return new ShellResult(-1, stdout.ToString(), $"timed out after {timeout?.TotalSeconds:F1}s\n{stderr}");
        }

        return new ShellResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
