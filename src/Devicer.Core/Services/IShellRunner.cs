namespace Devicer.Core.Services;

public sealed record ShellResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Success => ExitCode == 0;
}

public interface IShellRunner
{
    Task<ShellResult> RunAsync(string fileName, IEnumerable<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}
