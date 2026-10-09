namespace Weir.Tray;

/// <summary>
/// The first-run question "Start Weir when you sign in to Windows?". Nothing starts with Windows unless the person says yes
/// here or later ticks "Start with Windows" in the tray menu. A silent start, or one with no desktop to ask on, asks
/// nothing and leaves it off.
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
        if (ask())
        {
            startup.Enable();
        }
        else
        {
            TrayLog.Write("Weir does not start with Windows: the answer was no.");
        }
    }

    private static bool AskInMessageBox() =>
        MessageBox.Show(Question, "Weir", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
}
