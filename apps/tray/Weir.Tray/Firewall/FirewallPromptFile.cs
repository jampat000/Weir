namespace Weir.Tray.Firewall;

/// <summary>
/// Whether the first-run "Allow Weir on your network?" prompt has already been shown, so it asks at most once.
/// Recorded under the runtime home with <see cref="AtomicFile"/>, so a crash mid-write cannot leave a file that
/// reads as "already asked" when it was not.
/// </summary>
static class FirewallPromptFile
{
    internal const string FileName = "firewall-prompt-answered";

    internal static bool AlreadyAsked(string runtimeHome) => File.Exists(Path.Combine(runtimeHome, FileName));

    internal static void MarkAsked(string runtimeHome, FirewallElevation.Outcome outcome) =>
        AtomicFile.WriteAllText(runtimeHome, FileName, outcome.ToString());
}
