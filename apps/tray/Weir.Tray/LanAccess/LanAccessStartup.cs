using System.Runtime.InteropServices;
using Weir.Tray.Firewall;

namespace Weir.Tray.LanAccess;

/// <summary>
/// Decides who the server listens for when the tray starts. A saved choice is final. Without one, this is an
/// install that has never chosen (a fresh install, or one that predates the setting): it keeps whatever Windows
/// already allows, so a PC other devices can reach today stays reachable after updating, and a PC nothing was ever
/// allowed on listens for this PC only.
/// </summary>
static class LanAccessStartup
{
    internal static ListenScope Resolve(string runtimeHome, string installRoot, Func<IFirewallPolicy> openPolicy, Action<string> log)
    {
        if (LanAccessSetting.Read(runtimeHome, log) is { } saved)
        {
            log($"LAN access: {saved.Describe()} (saved choice).");
            return saved;
        }

        bool allowedByWindows;
        try
        {
            allowedByWindows = WeirFirewallRule.AllowsServerInbound(openPolicy(), installRoot);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // Not saved, so the next start reads the firewall again instead of settling on a guess.
            log($"LAN access: Windows Firewall could not be read ({ex.Message}); listening for this PC only for now.");
            return ListenScope.ThisPcOnly;
        }

        var scope = allowedByWindows ? ListenScope.OtherDevices : ListenScope.ThisPcOnly;
        log($"LAN access: {scope.Describe()}; {(allowedByWindows ? "Windows Firewall already allows Weir's server in" : "Windows Firewall has no rule allowing Weir's server in")}. Saving that choice.");
        try
        {
            LanAccessSetting.Write(runtimeHome, scope);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"LAN access: the choice could not be saved ({ex.Message}); it is worked out again at the next start.");
        }
        return scope;
    }
}
