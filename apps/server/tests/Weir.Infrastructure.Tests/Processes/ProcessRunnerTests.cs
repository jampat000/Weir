using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Weir.Infrastructure.Processes;

namespace Weir.Infrastructure.Tests.Processes;

/// <summary>The real <see cref="ProcessRunner"/> on this OS's shell: capture, lines, timeouts, cancellation, tree kill.</summary>
public sealed class ProcessRunnerTests
{
    private static readonly ProcessRunner Runner = new();

    /// <summary>The long-lived child <see cref="ShellWithSleepingChild"/> starts underneath the shell.</summary>
    private static string LingeringChildProcessName => OperatingSystem.IsWindows() ? "ping" : "sleep";

    private static string[] Shell(string windows, string posix) =>
        OperatingSystem.IsWindows() ? ["cmd.exe", "/d", "/c", windows] : ["/bin/sh", "-c", posix];

    /// <summary>A shell that starts a long-lived child, so killing only the shell would leave the child holding the pipes.</summary>
    private static string[] ShellWithSleepingChild() =>
        Shell("ping -n 60 127.0.0.1 >NUL & echo done", "sleep 60; echo done");

    [Fact]
    public async Task Stdout_stderr_and_exit_code_are_captured()
    {
        var result = await Runner.RunAsync(new ProcessRequest { Argv = Shell("echo out& echo err 1>&2& exit /b 3", "echo out; echo err 1>&2; exit 3") });

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Equal("out", Encoding.UTF8.GetString(result.Stdout).Trim());
        Assert.Equal("err", Encoding.UTF8.GetString(result.Stderr).Trim());
    }

    [PosixFact("The shell's own argument handling is what this proves; dotnet itself echoes nothing useful on Windows.")]
    public async Task Arguments_reach_the_child_token_for_token()
    {
        var result = await Runner.RunAsync(new ProcessRequest { Argv = ["/bin/sh", "-c", "printf '%s|' \"$@\"", "sh", "a b", "'q'", "\"d\"", ""] });

        Assert.Equal("a b|'q'|\"d\"||", Encoding.UTF8.GetString(result.Stdout));
    }

    [Fact]
    public async Task Tail_keeps_only_the_last_bytes()
    {
        var result = await Runner.RunAsync(new ProcessRequest
        {
            Argv = Shell("for /l %i in (1,1,400) do @echo line %i", "i=1; while [ $i -le 400 ]; do echo line $i; i=$((i+1)); done"),
            Stdout = ProcessOutput.Tail,
            TailBytes = 64,
        });

        Assert.Equal(64, result.Stdout.Length);
        Assert.EndsWith("line 400", Encoding.UTF8.GetString(result.Stdout).TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lines_arrive_split_on_every_newline_style()
    {
        var lines = new List<string>();

        await Runner.RunAsync(new ProcessRequest
        {
            Argv = Shell("echo one& echo two", "printf 'one\\r\\ntwo\\rthree\\nfour'"),
            OnStdoutLine = lines.Add,
        });

        Assert.Equal(OperatingSystem.IsWindows() ? ["one", "two"] : ["one", "two", "three", "four"], lines);
    }

    [Fact]
    public async Task A_timeout_kills_the_whole_tree_and_returns_promptly()
    {
        var before = Process.GetProcessesByName(LingeringChildProcessName).Length;

        var result = await Runner.RunAsync(new ProcessRequest { Argv = ShellWithSleepingChild(), Timeout = TimeSpan.FromMilliseconds(500) });

        Assert.Equal(ProcessTimeoutKind.Overall, result.Timeout);
        Assert.DoesNotContain("done", Encoding.UTF8.GetString(result.Stdout), StringComparison.Ordinal);
        await Eventually.ThatAsync(() => Process.GetProcessesByName(LingeringChildProcessName).Length <= before);
    }

    [Fact]
    public async Task A_progress_run_that_never_writes_a_line_is_still_stopped_by_the_timer()
    {
        // #539 item 4: a progress loop that only checks its timeout as a line arrives would hang forever on a
        // process that never writes one - stuck reading its input, for instance.
        // Here the child (ping/sleep, redirected to NUL/dev-null) writes nothing until well after "echo done",
        // which never runs within the timeout; the timeout is still enforced, on the wall-clock timer alone.
        var lines = new List<string>();
        var before = Process.GetProcessesByName(LingeringChildProcessName).Length;

        var result = await Runner.RunAsync(new ProcessRequest
        {
            Argv = ShellWithSleepingChild(),
            OnStdoutLine = lines.Add,
            Timeout = TimeSpan.FromMilliseconds(500),
        });

        Assert.Equal(ProcessTimeoutKind.Overall, result.Timeout);
        Assert.Empty(lines);
        await Eventually.ThatAsync(() => Process.GetProcessesByName(LingeringChildProcessName).Length <= before);
    }

    [Fact]
    public async Task Cancellation_kills_the_tree_and_throws()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner.RunAsync(new ProcessRequest { Argv = ShellWithSleepingChild() }, cancel.Token));
    }

    [Fact]
    public async Task A_throwing_line_callback_kills_the_process_and_rethrows()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Runner.RunAsync(new ProcessRequest
        {
            // The line comes from the long-lived process itself. A line the shell echoes before starting its child
            // races the child's creation: under load the tree kill can run before the child exists to be found.
            Argv = Shell("ping -n 60 127.0.0.1", "echo first; exec sleep 60"),
            OnStdoutLine = _ => throw new InvalidOperationException("stop"),
        }));

        Assert.Equal("stop", error.Message);
    }

    [PosixFact("cmd.exe cannot close its own stdout while the child keeps running; this proves the exit-after-close grace.")]
    public async Task A_process_that_lingers_after_closing_stdout_is_killed_after_the_grace()
    {
        var result = await Runner.RunAsync(new ProcessRequest
        {
            Argv = ["/bin/sh", "-c", "exec 1>&-; sleep 60"],
            OnStdoutLine = _ => { },
            ExitTimeoutAfterStdoutClosed = TimeSpan.FromMilliseconds(300),
            Timeout = TimeSpan.FromSeconds(30),
        });

        Assert.True(result.TimedOut);
    }

    [Fact]
    public async Task A_missing_executable_throws()
    {
        await Assert.ThrowsAsync<Win32Exception>(() => Runner.RunAsync(new ProcessRequest { Argv = ["weir-no-such-tool-" + Guid.NewGuid().ToString("N")] }));
    }
}
