namespace Weir.Tray.Firewall;

/// <summary>Whether a rule allows or blocks traffic.</summary>
enum FirewallRuleAction
{
    Allow,
    Block,
}

/// <summary>Which way the traffic in a rule travels.</summary>
enum FirewallRuleDirection
{
    Inbound,
    Outbound,
}

/// <summary>
/// The Windows Firewall network profiles a rule applies to. Values match <c>NET_FW_PROFILE_TYPE2_</c> in
/// <c>INetFwPolicy2</c> (Domain=1, Private=2, Public=4), so they can be read from and written to the COM API
/// without translation.
/// </summary>
[Flags]
enum FirewallProfiles
{
    None = 0,
    Domain = 1,
    Private = 2,
    Public = 4,
}

/// <summary>
/// One inbound or outbound firewall rule, as much of it as Weir's own logic needs to read or write. This is the
/// seam between the rule decisions in <see cref="WeirFirewallRule"/> and <see cref="IFirewallPolicy"/>'s COM
/// implementation, so the decisions are testable with a fake policy instead of the real firewall.
/// </summary>
sealed record FirewallRule(
    string Name,
    string ProgramPath,
    FirewallRuleAction Action,
    FirewallRuleDirection Direction,
    FirewallProfiles Profiles,
    bool Enabled);
