namespace Weir.Tray;

/// <summary>How long a tray balloon stays: information briefly, a problem the person must read for longer.</summary>
static class TrayBalloons
{
    private const int InformationMs = 10_000;
    private const int ProblemMs = 30_000;

    internal static int TimeoutMs(ToolTipIcon icon) => icon is ToolTipIcon.Warning or ToolTipIcon.Error ? ProblemMs : InformationMs;

    /// <summary>The server exited and the watchdog gave up bringing it back. A click restarts it.</summary>
    internal const string StoppedText = "Weir stopped and couldn't start again by itself. Click to restart it.";

    /// <summary>Weir could not start, or could not start again from Restart Weir. A click tries again.</summary>
    internal const string CouldNotStartText = "Weir couldn't start. Click to try again.";

    /// <summary>The person clicked the icon or Open Weir while the server was starting.</summary>
    internal const string StillStartingText = "Weir is still starting. Try again in a moment.";

    /// <summary>The person clicked the icon or Open Weir while the server was stopped.</summary>
    internal const string NotRunningText = "Weir isn't running. Click to restart it.";

    /// <summary>Weir has started, and the person started it. A click opens it.</summary>
    internal static string RunningText(int port) => $"Weir is running at {TrayState.AddressFor(port)}. Click to open it.";

    /// <summary>Weir was started again while it was running.</summary>
    internal static string AlreadyRunningText(int port) => $"Weir is already running at {TrayState.AddressFor(port)}";

    /// <summary>Change port worked. A click opens Weir at the new address.</summary>
    internal static string PortMovedText(int port) => $"Weir is now at {TrayState.AddressFor(port)}. Click to open it.";
}
