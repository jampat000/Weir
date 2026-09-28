using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Weir.Infrastructure.Processes;

namespace Weir.Infrastructure.Tests.Processes;

/// <summary>The real <see cref="ProcessRunner"/> on this OS's shell: capture, lines, timeouts, cancellation, tree kill.</summary>
public sealed class ProcessRunnerTests
{
    private static readonly ProcessRunner Runner = new();

    private const string StandInName = "Weir.TestChild";

    /// <summary>Long enough for the stand-in to have started its own child, so a kill always finds the whole tree.</summary>
    private static readonly TimeSpan KillAfter = TimeSpan.FromSeconds(2);

    private static string StandInPath { get; } = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? StandInName + ".exe" : StandInName);

    private static string[] Shell(string windows, string posix) =>
        OperatingSystem.IsWindows() ? ["cmd.exe", "/d", "/c", windows] : ["/bin/sh", "-c", posix];

    /// <summary>A slow tool that says something first and never finishes on its own.</summary>
    private static string[] TalkativeSlowTool() => [StandInPath, "announce-and-hold"];

    /// <summary>
    /// A tool that starts a slow child of its own and waits for it, so killing only the tool would leave the child
    /// holding the pipes. It prints a line only after that child ends.
    /// </summary>
    private static string[] ToolWithSlowChild() => [StandInPath, "hold-through-child"];

    private static int RunningStandIns() => Process.GetProcessesByName(StandInName).Length;

    [Fact]
    public async Task Stdout_stderr_and_exit_code_are_captured()
    {
        var result = await Runner.RunAsync(new ProcessRequest { Argv = Shell("echo out& echo err 1>&2& exit /b 3", "echo out; echo err 1>&2; exit 3") });

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Equal("out", Encoding.UTF8.GetString(result.Stdout).Trim());
        Assert.Equal("err", Encoding.UTF8.GetString(result.Stderr).Trim());
    }

    [Fact]
    public async Task A_tool_runs_below_normal_priority()
    {
        // The child reads stdin to its end before reporting, and the runner closes stdin only after it has set the
        // priority, so the child cannot look before it is set.
        string[] reportOwnPriority = OperatingSystem.IsWindows()
            ? ["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", "[Console]::In.ReadToEnd() | Out-Null; [System.Diagnostics.Process]::GetCurrentProcess().PriorityClass"]
            : ["/bin/sh", "-c", "cat > /dev/null; awk '{ print $19 }' /proc/$$/stat"];

        var result = await Runner.RunAsync(new ProcessRequest { Argv = reportOwnPriority });

        Assert.Equal(OperatingSystem.IsWindows() ? "BelowNormal" : "10", Encoding.UTF8.GetString(result.Stdout).Trim());
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
        var before = RunningStandIns();

        var result = await Runner.RunAsync(new ProcessRequest { Argv = ToolWithSlowChild(), Timeout = KillAfter });

        Assert.Equal(ProcessTimeoutKind.Overall, result.Timeout);
        Assert.DoesNotContain("done", Encoding.UTF8.GetString(result.Stdout), StringComparison.Ordinal);
        await Eventually.ThatAsync(() => RunningStandIns() <= before);
    }

    [Fact]
    public async Task A_progress_run_that_never_writes_a_line_is_still_stopped_by_the_timer()
    {
        // #539 item 4: a progress loop that only checks its timeout as a line arrives would hang forever on a
        // process that never writes one - stuck reading its input, for instance.
        // Here the tool writes nothing until its child ends, which is long after the timeout; the timeout is still
        // enforced, on the wall-clock timer alone.
        var lines = new List<string>();
        var before = RunningStandIns();

        var result = await Runner.RunAsync(new ProcessRequest
        {
            Argv = ToolWithSlowChild(),
            OnStdoutLine = lines.Add,
            Timeout = KillAfter,
        });

        Assert.Equal(ProcessTimeoutKind.Overall, result.Timeout);
        Assert.Empty(lines);
        await Eventually.ThatAsync(() => RunningStandIns() <= before);
    }

    [Fact]
    public async Task Cancellation_kills_the_tree_and_throws()
    {
        using var cancel = new CancellationTokenSource(KillAfter);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner.RunAsync(new ProcessRequest { Argv = ToolWithSlowChild() }, cancel.Token));
    }

    [Fact]
    public async Task A_throwing_line_callback_kills_the_process_and_rethrows()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Runner.RunAsync(new ProcessRequest
        {
            Argv = TalkativeSlowTool(),
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
