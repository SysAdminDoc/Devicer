using Devicer.Core.Services;

namespace Devicer.Core.Tests;

public class CaptureIsolationTests
{
    [Fact]
    public async Task Disabled_runner_rejects_commands_before_starting_a_process()
    {
        var runner = new ShellRunner(allowProcesses: false);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync("nonexistent-capture-sentinel.exe", ["devices"]));
        Assert.Contains("disabled in sample capture mode", exception.Message);
    }
}
