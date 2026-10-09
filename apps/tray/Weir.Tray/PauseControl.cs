namespace Weir.Tray;

/// <summary>
/// The tray menu's Pause processing and Resume processing. It asks the server through pause-request.json and shows nothing
/// of its own: the server answers by rewriting tray-status.json, which is what the icon and the menu then show.
/// </summary>
/// <param name="runtimeHome">The data folder.</param>
/// <param name="clock">Stamps the request.</param>
/// <param name="isPaused">Whether processing is paused as far as the tray knows.</param>
/// <param name="balloon">Shows a balloon: title, text, icon and what a click on it does.</param>
/// <param name="showLog">What a click on the balloon about a failed request does: shows tray-host.log.</param>
sealed class PauseControl(
    string runtimeHome,
    TimeProvider clock,
    Func<bool> isPaused,
    Action<string, string, ToolTipIcon, Action?> balloon,
    Action showLog)
{
    /// <summary>Asks the server to pause processing, or to resume it if it is paused.</summary>
    internal void Toggle()
    {
        var pause = !isPaused();
        try
        {
            PauseRequestFile.Write(runtimeHome, pause, clock.GetUtcNow());
            TrayLog.Write($"{(pause ? "Pause" : "Resume")} processing asked for from the tray menu.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TrayLog.Write($"Could not ask the server to {(pause ? "pause" : "resume")}: {ex.Message}");
            balloon(
                "Weir",
                $"Weir couldn't {(pause ? "pause" : "resume")} processing. See tray-host.log in the data folder.",
                ToolTipIcon.Warning,
                showLog);
        }
    }
}
