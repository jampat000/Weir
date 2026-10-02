namespace Weir.Tray.Firewall;

/// <summary>
/// What the LAN access watcher needs of Windows Firewall: whether Weir's server is already let in, and a way to ask
/// the person on this PC to allow it. Narrow enough to fake in tests; <see cref="WindowsFirewallAccess"/> is the real one.
/// </summary>
interface IFirewallAccess
{
    /// <summary>Whether an enabled allow rule already covers Weir's server. Read-only; needs no administrator rights.</summary>
    bool AllowsWeirIn();

    /// <summary>
    /// Creates the rule through Windows' own administrator prompt on this PC (Private and Domain networks only), and
    /// returns once the person has answered it. Blocks until then.
    /// </summary>
    FirewallElevation.Outcome AskToAllow();
}
