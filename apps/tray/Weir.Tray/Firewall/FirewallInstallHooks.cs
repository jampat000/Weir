using System.Runtime.InteropServices;
using Weir.Tray.LanAccess;

namespace Weir.Tray.Firewall;

/// <summary>
/// What the install and uninstall hooks do about Windows Firewall: the first-run question, and removing the rule
/// on the way out.
/// </summary>
static class FirewallInstallHooks
{
    /// <summary>
    /// The one Windows admin (UAC) prompt Weir ever asks for on its own, and only once: <see cref="FirewallPromptFile"/>
    /// records the answer, even a decline, so a person who said no is never asked again at every start. "Allow other
    /// devices on your network..." in the tray menu is how they revisit it later. A yes that ends with the rule in
    /// place also turns LAN access on; anything else leaves Weir listening for this PC only.
    /// </summary>
    internal static void AskOnFirstRun(string[] args)
    {
        if (!Program.ShouldPromptForFirewallAccess(args, PortChoice.HasInteractiveDesktop))
        {
            TrayLog.Write("First-run firewall prompt skipped: silent start or no interactive desktop.");
            return;
        }

        var runtimeHome = Program.RuntimeHome();
        try
        {
            if (FirewallPromptFile.AlreadyAsked(runtimeHome))
            {
                return;
            }

            var allow = MessageBox.Show(
                "Other devices on your network, such as Deluno, need Weir allowed through Windows Firewall to reach it. Allow Weir on your network?",
                "Weir",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes;

            var outcome = allow ? FirewallElevation.ConfigureElevated(TrayLog.Write) : FirewallElevation.Outcome.Declined;
            FirewallPromptFile.MarkAsked(runtimeHome, outcome);
            if (outcome == FirewallElevation.Outcome.Configured)
            {
                LanAccessSetting.Write(runtimeHome, ListenScope.OtherDevices);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TrayLog.Write($"First-run firewall prompt could not record its answer: {ex.Message}");
        }
    }

    /// <summary>
    /// A FastCallback hook has a 30-second budget and must show nothing (Velopack.VelopackApp docs), so this never
    /// asks for elevation — it only acts when the uninstaller already happens to be running elevated. Left in place
    /// otherwise, the rule is harmless: it names a program that will not exist once uninstall finishes.
    /// </summary>
    internal static void RemoveRuleIfElevated()
    {
        if (!FirewallCommand.IsElevated())
        {
            TrayLog.Write("Uninstall: leaving the Weir firewall rule in place (not running elevated).");
            return;
        }
        try
        {
            WeirFirewallRule.Remove(new ComFirewallPolicy());
            TrayLog.Write("Uninstall: removed the Weir firewall rule.");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            TrayLog.Write($"Uninstall: could not remove the Weir firewall rule: {ex.Message}");
        }
    }
}
