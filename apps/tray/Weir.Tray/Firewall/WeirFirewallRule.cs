namespace Weir.Tray.Firewall;

/// <summary>
/// The one firewall rule Weir manages, and the decisions around it: what it looks like, which of the firewall's
/// other rules count as blocking it, and whether Windows already lets other devices in. Pure logic over
/// <see cref="IFirewallPolicy"/>, so it is testable without the real Windows Firewall.
/// </summary>
static class WeirFirewallRule
{
    /// <summary>The one rule Weir creates. A fixed name so there is only ever one, and it is easy to find again.</summary>
    internal const string RuleName = "Weir";

    /// <summary>Where the bundled server lives under a Velopack install, relative to the <c>current</c> folder.</summary>
    internal const string ServerRelativePath = @"server\WeirServer.exe";

    /// <summary>
    /// Weir listens on TCP only; the rule never opens UDP or any other protocol it does not use.
    /// </summary>
    internal const string Protocol = "TCP";

    /// <summary>
    /// Every network profile, Public included: Windows marks many home networks Public, and a rule that skipped them
    /// would leave "Devices on my network" doing nothing there. Weir is still behind its own sign-in.
    /// </summary>
    internal const FirewallProfiles AllowedProfiles = FirewallProfiles.Domain | FirewallProfiles.Private | FirewallProfiles.Public;

    /// <summary>
    /// The server executable's path, scoped to Velopack's stable <c>current</c> folder
    /// (<see cref="InstallProcesses.Root"/>) rather than a versioned one, so the rule keeps matching the running
    /// server across every update without being touched again.
    /// </summary>
    internal static string ServerProgramPath(string installRoot) =>
        Path.GetFullPath(Path.Combine(installRoot, ServerRelativePath));

    /// <summary>The rule Weir wants in place: one inbound TCP allow rule for its own server, on every network profile.</summary>
    internal static FirewallRule DesiredRule(string installRoot) => new(
        RuleName,
        ServerProgramPath(installRoot),
        FirewallRuleAction.Allow,
        FirewallRuleDirection.Inbound,
        AllowedProfiles,
        Enabled: true);

    /// <summary>
    /// Adds or updates Weir's allow rule (an older one that covers fewer profiles, or another program path, is
    /// replaced by the current one), and removes every inbound block rule that targets Weir's own server exe
    /// (for example one Windows created when its own "blocked some features" prompt was cancelled). Never touches
    /// a rule for any other program. Idempotent: running it again when everything already matches changes nothing.
    /// </summary>
    internal static FirewallChangeSummary Configure(IFirewallPolicy policy, string installRoot)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var programPath = ServerProgramPath(installRoot);
        policy.AddOrUpdateRule(DesiredRule(installRoot));

        // Snapshotted first: removing a rule while still enumerating the collection it came from is not safe,
        // whether that collection is this in-memory list (tests) or a COM one materialized fresh per read.
        var blockRules = OwnBlockRules(policy, programPath).ToList();
        foreach (var blockRule in blockRules)
        {
            policy.RemoveRule(blockRule.Name);
        }
        return new FirewallChangeSummary(AllowRuleWritten: true, BlockRulesRemoved: blockRules.Count);
    }

    /// <summary>Removes Weir's allow rule. Leaves any block rules alone: they are not Weir's to manage on the way out.</summary>
    internal static void Remove(IFirewallPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.RemoveRule(RuleName);
    }

    /// <summary>
    /// Every inbound block rule that targets exactly Weir's own server exe at <paramref name="programPath"/>.
    /// Windows compares program paths case-insensitively, and so does this.
    /// </summary>
    internal static IEnumerable<FirewallRule> OwnBlockRules(IFirewallPolicy policy, string programPath) =>
        policy.Rules.Where(rule =>
            rule.Direction == FirewallRuleDirection.Inbound
            && rule.Action == FirewallRuleAction.Block
            && string.Equals(rule.ProgramPath, programPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether Windows already lets other devices reach Weir's server on at least one kind of network: an enabled
    /// inbound allow rule for its program, whether Weir's own <see cref="RuleName"/> rule or one Windows made when
    /// someone clicked Allow on its prompt, on a profile no enabled block rule for it covers (a block wins). The
    /// PC's current network is deliberately not consulted: the answer is whether this PC has been open to other
    /// devices, so the same devices stay reachable when it moves to another network. Read-only; needs no
    /// administrator rights.
    /// </summary>
    internal static bool AllowsServerInbound(IFirewallPolicy policy, string installRoot)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var programPath = ServerProgramPath(installRoot);

        var blocked = ProfilesOf(OwnBlockRules(policy, programPath));
        var allowed = ProfilesOf(policy.Rules.Where(rule =>
            rule.Direction == FirewallRuleDirection.Inbound
            && rule.Action == FirewallRuleAction.Allow
            && string.Equals(rule.ProgramPath, programPath, StringComparison.OrdinalIgnoreCase)));
        return (allowed & ~blocked) != FirewallProfiles.None;
    }

    /// <summary>
    /// Whether Windows lets other devices reach Weir's server on the network this PC is on right now: an enabled
    /// inbound allow rule for its program covers a current profile and no enabled block rule for it does. Unlike
    /// <see cref="AllowsServerInbound"/>, a rule for other profiles only does not count, so a rule written before
    /// Public was covered is asked for again when the PC is on a Public network. Read-only; needs no administrator rights.
    /// </summary>
    internal static bool AllowsServerOnCurrentNetwork(IFirewallPolicy policy, string installRoot)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var programPath = ServerProgramPath(installRoot);
        var current = policy.CurrentProfiles;

        var blocked = ProfilesOf(OwnBlockRules(policy, programPath));
        var allowed = ProfilesOf(policy.Rules.Where(rule =>
            rule.Direction == FirewallRuleDirection.Inbound
            && rule.Action == FirewallRuleAction.Allow
            && string.Equals(rule.ProgramPath, programPath, StringComparison.OrdinalIgnoreCase)));
        return (allowed & current) != FirewallProfiles.None && (blocked & current) == FirewallProfiles.None;
    }

    private static FirewallProfiles ProfilesOf(IEnumerable<FirewallRule> rules) =>
        rules.Where(rule => rule.Enabled).Aggregate(FirewallProfiles.None, (all, rule) => all | rule.Profiles);
}

/// <summary>What <see cref="WeirFirewallRule.Configure"/> did, for logging and exit-code decisions.</summary>
sealed record FirewallChangeSummary(bool AllowRuleWritten, int BlockRulesRemoved);
