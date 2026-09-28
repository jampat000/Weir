namespace Weir.Tray.Firewall;

/// <summary>
/// Whether the first-run "Allow Weir on your network?" prompt has already been shown, so it asks at most once.
/// Recorded under the runtime home the same way the saved port is (<see cref="PortChoice.Save"/>): written whole
/// to a scratch file, then renamed into place, so a crash mid-write cannot leave a file that reads as
/// "already asked" when it was not.
/// </summary>
static class FirewallPromptFile
{
    internal const string FileName = "firewall-prompt-answered";

    internal static bool AlreadyAsked(string runtimeHome) => File.Exists(Path.Combine(runtimeHome, FileName));

    internal static void MarkAsked(string runtimeHome, FirewallElevation.Outcome outcome)
    {
        Directory.CreateDirectory(runtimeHome);
        var path = Path.Combine(runtimeHome, FileName);
        var tmp = Path.Combine(runtimeHome, $".{FileName}.{Guid.NewGuid():n}.tmp");
        try
        {
            File.WriteAllText(tmp, outcome.ToString());
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            // Only still there when the write or the rename failed.
            File.Delete(tmp);
        }
    }
}
