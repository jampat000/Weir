using Weir.Tray.Firewall;

namespace Weir.Tray.Tests.Firewall;

/// <summary>
/// An in-memory stand-in for the real Windows Firewall, so <see cref="WeirFirewallRule"/>'s decisions and
/// <see cref="FirewallCommand"/>'s argument handling are tested without touching COM or needing admin rights.
/// </summary>
internal sealed class FakeFirewallPolicy : IFirewallPolicy
{
    private readonly List<FirewallRule> _rules = [];

    internal FakeFirewallPolicy(params FirewallRule[] seedRules) => _rules.AddRange(seedRules);

    public IReadOnlyList<FirewallRule> Rules => _rules;

    public FirewallProfiles CurrentProfiles { get; set; } = FirewallProfiles.Private;

    public void AddOrUpdateRule(FirewallRule rule)
    {
        _rules.RemoveAll(r => r.Name == rule.Name);
        _rules.Add(rule);
    }

    public void RemoveRule(string name) => _rules.RemoveAll(r => r.Name == name);
}
