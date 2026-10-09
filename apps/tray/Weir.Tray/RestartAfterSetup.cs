using System.ComponentModel;
using System.Diagnostics;

namespace Weir.Tray;

/// <summary>
/// Weir comes back after a Setup run over a running Weir, when Setup is silent. Setup stops the running tray and server
/// before it runs any hook, and starts the new Weir itself only when it is not silent, so a silent over-install would
/// otherwise leave Weir stopped (#942).
///
/// The after-install hook cannot start the tray: Setup stops every process running from the install folder again when the
/// hook ends. So the hook starts a short PowerShell, which runs from System32, waits for Setup to exit and then starts the
/// tray with <see cref="Argument"/>. That start leaves the work to a tray that is already running, which is what a plain
/// Setup has just started, so a plain Setup is not started twice and keeps Velopack's own start.
/// </summary>
static class RestartAfterSetup
{
    /// <summary>
    /// Added to the start that follows Setup: start Weir, unless one is already running from this install. Whether Setup
    /// was silent is not asked, because what matters is whether Setup started Weir, and that has happened by the time
    /// Setup exits.
    /// </summary>
    internal const string Argument = "--after-setup";

    private static readonly TimeSpan SetupCeiling = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Arranges for Weir to start once Setup has exited, when it was running before. Nothing is arranged when it was not:
    /// an install must not start a Weir that was stopped.
    /// </summary>
    internal static void Schedule(
        TrayRunningMark wasRunning,
        string executable,
        Func<int?> setupProcessId,
        Action<ProcessStartInfo> start,
        Action<string> log)
    {
        if (!wasRunning.Take())
        {
            log("Weir was not running before this install, so it is left stopped.");
            return;
        }

        if (setupProcessId() is not { } setup)
        {
            log("Weir was running before this install, but Setup could not be identified, so it is not started again.");
            return;
        }

        try
        {
            start(HelperStart(setup, executable));
            log($"Weir was running before this install. It starts again once Setup (pid {setup}) has exited, unless Setup starts it.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            log($"Weir was running before this install, but it could not be started again: {ex.Message}");
        }
    }

    /// <summary>
    /// Whether this start, which follows Setup, has nothing to do because a tray is already running. The question is asked
    /// only for a start that carries <see cref="Argument"/>.
    /// </summary>
    internal static bool ShouldStepAside(IEnumerable<string> args, Func<bool> anotherTrayRuns) =>
        args.Contains(Argument) && anotherTrayRuns();

    /// <summary>
    /// The PowerShell that waits for Setup and then starts the tray, with no window. A Setup that has already exited is not
    /// waited for, and one that outlasts the ceiling is given up on rather than waited for forever.
    /// </summary>
    internal static ProcessStartInfo HelperStart(int setupProcessId, string executable)
    {
        var start = new ProcessStartInfo(PowerShellPath())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command", HelperScript(setupProcessId, executable) })
        {
            start.ArgumentList.Add(argument);
        }
        return start;
    }

    internal static string HelperScript(int setupProcessId, string executable)
    {
        var path = executable.Replace("'", "''", StringComparison.Ordinal);
        var ceilingMilliseconds = (int)SetupCeiling.TotalMilliseconds;
        return
            $"$setup = Get-Process -Id {setupProcessId} -ErrorAction SilentlyContinue; " +
            $"if ($setup -and -not $setup.WaitForExit({ceilingMilliseconds})) {{ exit 1 }}; " +
            $"Start-Process -FilePath '{path}' -ArgumentList '{Program.NoBrowserArgument}','{Argument}'";
    }

    private static string PowerShellPath() =>
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
}
