using Weir.Tray.Firewall;
using Xunit;

namespace Weir.Tray.Tests.Firewall;

/// <summary>
/// The rule Weir asks Windows Firewall for, and the decisions around it: what it looks like, which of the
/// firewall's other rules count as blocking Weir specifically, and what state that adds up to for System › About.
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
    public void The_desired_rule_allows_inbound_tcp_on_private_and_domain_only()
    {
        var rule = WeirFirewallRule.DesiredRule(InstallRoot);

        Assert.Equal(FirewallRuleAction.Allow, rule.Action);
        Assert.Equal(FirewallRuleDirection.Inbound, rule.Direction);
        Assert.Equal(FirewallProfiles.Domain | FirewallProfiles.Private, rule.Profiles);
        Assert.True(rule.Enabled);
    }

    [Fact]
    public void The_desired_rule_never_includes_the_public_profile()
    {
        var rule = WeirFirewallRule.DesiredRule(InstallRoot);

        Assert.Equal(FirewallProfiles.None, rule.Profiles & FirewallProfiles.Public);
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
        Assert.Equal(FirewallProfiles.Domain | FirewallProfiles.Private, rule.Profiles);
        Assert.True(rule.Enabled);
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

    // -- Reading state for System › About ------------------------------------

    [Fact]
    public void State_is_not_configured_when_there_is_no_rule_at_all()
    {
        var policy = new FakeFirewallPolicy { CurrentProfiles = FirewallProfiles.Private };

        Assert.Equal(NetworkAccessState.NotConfigured, WeirFirewallRule.ReadState(policy, InstallRoot));
    }

    [Fact]
    public void State_is_allowed_when_the_rule_is_enabled_and_covers_the_current_network()
    {
        var policy = new FakeFirewallPolicy(WeirFirewallRule.DesiredRule(InstallRoot)) { CurrentProfiles = FirewallProfiles.Private };

        Assert.Equal(NetworkAccessState.Allowed, WeirFirewallRule.ReadState(policy, InstallRoot));
    }

    [Fact]
    public void State_is_blocked_when_the_rule_is_disabled()
    {
        var disabled = WeirFirewallRule.DesiredRule(InstallRoot) with { Enabled = false };
        var policy = new FakeFirewallPolicy(disabled) { CurrentProfiles = FirewallProfiles.Private };

        Assert.Equal(NetworkAccessState.Blocked, WeirFirewallRule.ReadState(policy, InstallRoot));
    }

    [Fact]
    public void State_is_blocked_when_the_current_network_is_public_and_the_rule_never_covers_it()
    {
        var policy = new FakeFirewallPolicy(WeirFirewallRule.DesiredRule(InstallRoot)) { CurrentProfiles = FirewallProfiles.Public };

        Assert.Equal(NetworkAccessState.Blocked, WeirFirewallRule.ReadState(policy, InstallRoot));
    }

    [Fact]
    public void State_is_blocked_when_a_block_rule_targets_weirs_own_exe_even_alongside_an_allow_rule()
    {
        var blocked = new FirewallRule("Blocked WeirServer.exe", ExpectedProgramPath, FirewallRuleAction.Block, FirewallRuleDirection.Inbound, FirewallProfiles.Public, Enabled: true);
        var policy = new FakeFirewallPolicy(WeirFirewallRule.DesiredRule(InstallRoot), blocked) { CurrentProfiles = FirewallProfiles.Private };

        Assert.Equal(NetworkAccessState.Blocked, WeirFirewallRule.ReadState(policy, InstallRoot));
    }
}
