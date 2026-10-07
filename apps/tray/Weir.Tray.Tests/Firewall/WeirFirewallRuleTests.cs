using Weir.Tray.Firewall;
using Xunit;

namespace Weir.Tray.Tests.Firewall;

/// <summary>
/// The rule Weir asks Windows Firewall for, and the decisions around it: what it looks like, which of the
/// firewall's other rules count as blocking Weir specifically, and whether Windows already lets other devices in.
/// </summary>
public sealed class WeirFirewallRuleTests
{
    private const string InstallRoot = @"C:\Users\test\AppData\Local\Weir\current";
    private static readonly string ExpectedProgramPath = System.IO.Path.Combine(InstallRoot, "server", "WeirServer.exe");

    // -- The rule definition ---------------------------------------------

    [Fact]
    public void The_rule_is_named_Weir()
    {
        Assert.Equal("Weir", WeirFirewallRule.RuleName);
    }

    [Fact]
    public void The_program_path_is_the_bundled_server_under_the_stable_current_folder()
    {
        Assert.Equal(ExpectedProgramPath, WeirFirewallRule.ServerProgramPath(InstallRoot));
    }

    [Fact]
    public void The_desired_rule_allows_inbound_on_every_network_profile()
    {
        var rule = WeirFirewallRule.DesiredRule(InstallRoot);

        Assert.Equal(FirewallRuleAction.Allow, rule.Action);
        Assert.Equal(FirewallRuleDirection.Inbound, rule.Direction);
        Assert.Equal(FirewallProfiles.Domain | FirewallProfiles.Private | FirewallProfiles.Public, rule.Profiles);
        Assert.True(rule.Enabled);
    }

    // -- Configure: adding the allow rule ---------------------------------

    [Fact]
    public void Configure_adds_the_allow_rule_when_none_exists()
    {
        var policy = new FakeFirewallPolicy();

        WeirFirewallRule.Configure(policy, InstallRoot);

        var rule = Assert.Single(policy.Rules);
        Assert.Equal("Weir", rule.Name);
        Assert.Equal(ExpectedProgramPath, rule.ProgramPath);
    }

    [Fact]
    public void Configure_is_idempotent()
    {
        var policy = new FakeFirewallPolicy();

        WeirFirewallRule.Configure(policy, InstallRoot);
        WeirFirewallRule.Configure(policy, InstallRoot);

        Assert.Single(policy.Rules);
    }

    [Fact]
    public void Configure_replaces_a_stale_allow_rule_rather_than_duplicating_it()
    {
        var stale = new FirewallRule("Weir", @"C:\old\WeirServer.exe", FirewallRuleAction.Allow, FirewallRuleDirection.Inbound, FirewallProfiles.Public, Enabled: false);
        var policy = new FakeFirewallPolicy(stale);

        WeirFirewallRule.Configure(policy, InstallRoot);

        var rule = Assert.Single(policy.Rules);
        Assert.Equal(ExpectedProgramPath, rule.ProgramPath);
        Assert.Equal(FirewallProfiles.Domain | FirewallProfiles.Private | FirewallProfiles.Public, rule.Profiles);
        Assert.True(rule.Enabled);
    }

    [Fact]
    public void Configure_widens_a_rule_from_before_public_was_covered_to_every_profile()
    {
        var older = new FirewallRule("Weir", ExpectedProgramPath, FirewallRuleAction.Allow, FirewallRuleDirection.Inbound, FirewallProfiles.Domain | FirewallProfiles.Private, Enabled: true);
        var policy = new FakeFirewallPolicy(older);

        WeirFirewallRule.Configure(policy, InstallRoot);

        var rule = Assert.Single(policy.Rules);
        Assert.Equal(FirewallProfiles.Domain | FirewallProfiles.Private | FirewallProfiles.Public, rule.Profiles);
    }

    // -- Configure: removing block rules for Weir's own exe only ----------

    [Fact]
    public void Configure_removes_an_inbound_block_rule_for_weirs_own_exe()
    {
        var blocked = new FirewallRule("Blocked WeirServer.exe", ExpectedProgramPath, FirewallRuleAction.Block, FirewallRuleDirection.Inbound, FirewallProfiles.Domain | FirewallProfiles.Private | FirewallProfiles.Public, Enabled: true);
        var policy = new FakeFirewallPolicy(blocked);

        var summary = WeirFirewallRule.Configure(policy, InstallRoot);

        Assert.Equal(1, summary.BlockRulesRemoved);
        Assert.DoesNotContain(policy.Rules, r => r.Name == "Blocked WeirServer.exe");
    }

    [Fact]
    public void Configure_leaves_a_block_rule_for_a_different_program_alone()
    {
        var otherProgram = new FirewallRule("Blocked Other App", @"C:\Program Files\Other\other.exe", FirewallRuleAction.Block, FirewallRuleDirection.Inbound, FirewallProfiles.Public, Enabled: true);
        var policy = new FakeFirewallPolicy(otherProgram);

        WeirFirewallRule.Configure(policy, InstallRoot);

        Assert.Contains(policy.Rules, r => r.Name == "Blocked Other App");
    }

    [Fact]
    public void Configure_leaves_an_outbound_block_rule_for_weirs_own_exe_alone()
    {
        var outboundBlock = new FirewallRule("Outbound block", ExpectedProgramPath, FirewallRuleAction.Block, FirewallRuleDirection.Outbound, FirewallProfiles.Public, Enabled: true);
        var policy = new FakeFirewallPolicy(outboundBlock);

        var summary = WeirFirewallRule.Configure(policy, InstallRoot);

        Assert.Equal(0, summary.BlockRulesRemoved);
        Assert.Contains(policy.Rules, r => r.Name == "Outbound block");
    }

    [Fact]
    public void Configure_leaves_an_allow_rule_for_a_different_program_alone()
    {
        var otherAllow = new FirewallRule("Some Other App", @"C:\Program Files\Other\other.exe", FirewallRuleAction.Allow, FirewallRuleDirection.Inbound, FirewallProfiles.Domain | FirewallProfiles.Private | FirewallProfiles.Public, Enabled: true);
        var policy = new FakeFirewallPolicy(otherAllow);

        WeirFirewallRule.Configure(policy, InstallRoot);

        Assert.Contains(policy.Rules, r => r.Name == "Some Other App");
    }

    // -- Remove -------------------------------------------------------------

    [Fact]
    public void Remove_deletes_only_the_named_weir_rule()
    {
        var weirRule = WeirFirewallRule.DesiredRule(InstallRoot);
        var otherRule = new FirewallRule("Some Other App", @"C:\Program Files\Other\other.exe", FirewallRuleAction.Allow, FirewallRuleDirection.Inbound, FirewallProfiles.Private, Enabled: true);
        var policy = new FakeFirewallPolicy(weirRule, otherRule);

        WeirFirewallRule.Remove(policy);

        Assert.DoesNotContain(policy.Rules, r => r.Name == "Weir");
        Assert.Contains(policy.Rules, r => r.Name == "Some Other App");
    }

    [Fact]
    public void Remove_is_idempotent_when_there_is_no_rule_to_remove()
    {
        var policy = new FakeFirewallPolicy();

        var exception = Record.Exception(() => WeirFirewallRule.Remove(policy));

        Assert.Null(exception);
    }

    // -- Whether Windows already lets other devices in -----------------------

    private static FirewallRule InboundRule(string name, FirewallRuleAction action, FirewallProfiles profiles, bool enabled = true, string? program = null) =>
        new(name, program ?? ExpectedProgramPath, action, FirewallRuleDirection.Inbound, profiles, enabled);

    [Fact]
    public void Nothing_is_allowed_when_there_is_no_rule_at_all()
    {
        Assert.False(WeirFirewallRule.AllowsServerInbound(new FakeFirewallPolicy(), InstallRoot));
    }

    [Fact]
    public void Weirs_own_rule_allows_the_server_inbound()
    {
        var policy = new FakeFirewallPolicy(WeirFirewallRule.DesiredRule(InstallRoot));

        Assert.True(WeirFirewallRule.AllowsServerInbound(policy, InstallRoot));
    }

    [Fact]
    public void A_rule_windows_made_when_someone_clicked_allow_counts_too()
    {
        var policy = new FakeFirewallPolicy(InboundRule("WeirServer.exe", FirewallRuleAction.Allow, FirewallProfiles.Private));

        Assert.True(WeirFirewallRule.AllowsServerInbound(policy, InstallRoot));
    }

    [Fact]
    public void An_allow_rule_for_another_program_does_not_count()
    {
        var policy = new FakeFirewallPolicy(InboundRule("Other", FirewallRuleAction.Allow, FirewallProfiles.Private, program: @"C:otherOther.exe"));

        Assert.False(WeirFirewallRule.AllowsServerInbound(policy, InstallRoot));
    }

    [Fact]
    public void A_disabled_allow_rule_does_not_count()
    {
        var policy = new FakeFirewallPolicy(InboundRule("Weir", FirewallRuleAction.Allow, FirewallProfiles.Private, enabled: false));

        Assert.False(WeirFirewallRule.AllowsServerInbound(policy, InstallRoot));
    }

    [Fact]
    public void An_outbound_allow_rule_does_not_count()
    {
        var outbound = new FirewallRule("Weir", ExpectedProgramPath, FirewallRuleAction.Allow, FirewallRuleDirection.Outbound, FirewallProfiles.Private, Enabled: true);

        Assert.False(WeirFirewallRule.AllowsServerInbound(new FakeFirewallPolicy(outbound), InstallRoot));
    }

    [Fact]
    public void A_block_rule_on_the_same_network_wins_over_the_allow_rule()
    {
        var policy = new FakeFirewallPolicy(
            InboundRule("Weir", FirewallRuleAction.Allow, FirewallProfiles.Private),
            InboundRule("Blocked WeirServer.exe", FirewallRuleAction.Block, FirewallProfiles.Private));

        Assert.False(WeirFirewallRule.AllowsServerInbound(policy, InstallRoot));
    }

    [Fact]
    public void A_block_rule_on_another_network_leaves_the_allowed_network_open()
    {
        var policy = new FakeFirewallPolicy(
            InboundRule("Weir", FirewallRuleAction.Allow, FirewallProfiles.Private),
            InboundRule("Blocked WeirServer.exe", FirewallRuleAction.Block, FirewallProfiles.Public));

        Assert.True(WeirFirewallRule.AllowsServerInbound(policy, InstallRoot));
    }

    [Fact]
    public void A_disabled_block_rule_blocks_nothing()
    {
        var policy = new FakeFirewallPolicy(
            InboundRule("Weir", FirewallRuleAction.Allow, FirewallProfiles.Private),
            InboundRule("Blocked WeirServer.exe", FirewallRuleAction.Block, FirewallProfiles.Private, enabled: false));

        Assert.True(WeirFirewallRule.AllowsServerInbound(policy, InstallRoot));
    }

    // -- Whether Windows lets other devices in on the network this PC is on now ----

    [Fact]
    public void An_all_profiles_rule_allows_the_server_on_a_public_network()
    {
        var policy = new FakeFirewallPolicy(WeirFirewallRule.DesiredRule(InstallRoot)) { CurrentProfiles = FirewallProfiles.Public };

        Assert.True(WeirFirewallRule.AllowsServerOnCurrentNetwork(policy, InstallRoot));
    }

    [Fact]
    public void An_older_private_and_domain_rule_does_not_allow_the_server_on_a_public_network()
    {
        var policy = new FakeFirewallPolicy(InboundRule("Weir", FirewallRuleAction.Allow, FirewallProfiles.Domain | FirewallProfiles.Private))
        {
            CurrentProfiles = FirewallProfiles.Public,
        };

        Assert.False(WeirFirewallRule.AllowsServerOnCurrentNetwork(policy, InstallRoot));
    }

    [Fact]
    public void An_older_private_and_domain_rule_still_allows_the_server_on_a_private_network()
    {
        var policy = new FakeFirewallPolicy(InboundRule("Weir", FirewallRuleAction.Allow, FirewallProfiles.Domain | FirewallProfiles.Private))
        {
            CurrentProfiles = FirewallProfiles.Private,
        };

        Assert.True(WeirFirewallRule.AllowsServerOnCurrentNetwork(policy, InstallRoot));
    }

    [Fact]
    public void A_block_rule_on_the_current_network_wins_over_an_all_profiles_rule()
    {
        var policy = new FakeFirewallPolicy(
            WeirFirewallRule.DesiredRule(InstallRoot),
            InboundRule("Blocked WeirServer.exe", FirewallRuleAction.Block, FirewallProfiles.Public))
        {
            CurrentProfiles = FirewallProfiles.Public,
        };

        Assert.False(WeirFirewallRule.AllowsServerOnCurrentNetwork(policy, InstallRoot));
    }

    [Fact]
    public void A_block_rule_on_a_network_this_pc_is_not_on_changes_nothing()
    {
        var policy = new FakeFirewallPolicy(
            WeirFirewallRule.DesiredRule(InstallRoot),
            InboundRule("Blocked WeirServer.exe", FirewallRuleAction.Block, FirewallProfiles.Public))
        {
            CurrentProfiles = FirewallProfiles.Private,
        };

        Assert.True(WeirFirewallRule.AllowsServerOnCurrentNetwork(policy, InstallRoot));
    }

    [Fact]
    public void A_disabled_rule_does_not_allow_the_server_on_the_current_network()
    {
        var policy = new FakeFirewallPolicy(InboundRule("Weir", FirewallRuleAction.Allow, FirewallProfiles.Domain | FirewallProfiles.Private | FirewallProfiles.Public, enabled: false));

        Assert.False(WeirFirewallRule.AllowsServerOnCurrentNetwork(policy, InstallRoot));
    }
}
