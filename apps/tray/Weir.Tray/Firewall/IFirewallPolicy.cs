namespace Weir.Tray.Firewall;

/// <summary>
/// Abstraction over the Windows Firewall's rule store, narrow enough to fake in tests. The real implementation
/// (<see cref="ComFirewallPolicy"/>) is late-bound COM; everything that decides what to add, update or remove
/// (<see cref="WeirFirewallRule"/>) is written against this interface instead, so it never touches COM directly.
/// </summary>
interface IFirewallPolicy
{
    /// <summary>Every rule the firewall currently has, both Weir's own and everyone else's. Read-only; no admin rights needed.</summary>
    IReadOnlyList<FirewallRule> Rules { get; }

    /// <summary>The profiles of the networks this PC is connected to right now. Read-only; no admin rights needed.</summary>
    FirewallProfiles CurrentProfiles { get; }

    /// <summary>
    /// Creates <paramref name="rule"/>, or overwrites the existing rule with the same <see cref="FirewallRule.Name"/>
    /// so there is never more than one. Requires administrator rights.
    /// </summary>
    void AddOrUpdateRule(FirewallRule rule);

    /// <summary>Removes the rule named <paramref name="name"/>, if one exists. Requires administrator rights.</summary>
    void RemoveRule(string name);
}
