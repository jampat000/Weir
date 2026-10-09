namespace Weir.Tray;

/// <summary>
/// The question "Start Weir when you sign in to Windows?". Nothing starts with Windows unless the person says yes here or
/// later ticks "Start with Windows" in the tray menu. It is asked at first run, and once more for an entry that an older
/// version set at install without asking. A silent start, or one with no desktop to ask on, asks nothing and changes nothing.
/// </summary>
static class StartWithWindowsPrompt
{
    internal const string Question = "Start Weir when you sign in to Windows?";

    internal static void AskOnFirstRun(string[] args) =>
        AskOnFirstRun(args, PortChoice.HasInteractiveDesktop, AskInMessageBox, StartupRegistration.ForThisUser());

    internal static void AskOnFirstRun(IEnumerable<string> args, Func<bool> desktopCheck, Func<bool> ask, StartupRegistration startup)
    {
        if (!Program.HasInteractiveDesktop(args, desktopCheck))
        {
            TrayLog.Write("First-run start-with-Windows question skipped: silent start or no interactive desktop. Weir does not start with Windows.");
            return;
        }
        startup.Choose(ask());
    }

    internal static void AskIfSetWithoutAnswer(string[] args) =>
        AskIfSetWithoutAnswer(args, PortChoice.HasInteractiveDesktop, AskInMessageBox, StartupRegistration.ForThisUser());

    /// <summary>
    /// Asks, once, about an entry nobody chose. Only a start the person made themselves qualifies: not a silent one, one with
    /// no desktop, or one with <see cref="Program.NoBrowserArgument"/>, which is how the entry itself starts Weir at sign-in
    /// (and how an update restart and Deluno start it), none of which should put a question on the screen. A start that does
    /// not qualify changes nothing; the next one that does asks.
    /// </summary>
    internal static void AskIfSetWithoutAnswer(IEnumerable<string> args, Func<bool> desktopCheck, Func<bool> ask, StartupRegistration startup)
    {
        if (!startup.IsSetWithoutAnswer)
        {
            return;
        }
        if (!Program.HasInteractiveDesktop(args, desktopCheck) || args.Contains(Program.NoBrowserArgument))
        {
            TrayLog.Write("Start-with-Windows question for an entry nobody chose is waiting for a start the person makes themselves.");
            return;
        }
        TrayLog.Write("Weir starts with Windows, but nobody was asked: asking now.");
        startup.Choose(ask());
    }

    private static bool AskInMessageBox() =>
        MessageBox.Show(Question, "Weir", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
}
