namespace Weir.Tray;

/// <summary>
/// What a failed check or download says to the person, in place of Velopack's exception text. The exception itself goes
/// to tray-host.log.
/// </summary>
static class UpdateFailureText
{
    private const string SeeLog = " The tray menu's Open logs folder has the detail.";

    internal static string ForCheck(Exception exception) => exception switch
    {
        HttpRequestException or TaskCanceledException => "Weir could not reach GitHub to look for an update. Check the internet connection and try again.",
        _ => "Weir could not check for updates." + SeeLog,
    };

    internal static string ForDownload(Exception exception) => exception switch
    {
        HttpRequestException or TaskCanceledException => "Weir could not download the update. Check the internet connection and try again.",
        IOException or UnauthorizedAccessException => "Weir could not save the update on this PC. Check there is free disk space and try again.",
        _ => "Weir could not download the update." + SeeLog,
    };
}
