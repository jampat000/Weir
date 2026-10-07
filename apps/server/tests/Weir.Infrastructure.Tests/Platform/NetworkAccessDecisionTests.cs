using Weir.Infrastructure.Runtime;

namespace Weir.Infrastructure.Tests.Platform;

public sealed class NetworkAccessDecisionTests
{
    private const int Domain = 1;
    private const int Private = 2;
    private const int Public = 4;

    private static ServerFirewallRule Allow(int profiles, bool enabled = true) => new(IsAllow: true, enabled, profiles);

    private static ServerFirewallRule Block(int profiles) => new(IsAllow: false, Enabled: true, profiles);

    [Fact]
    public void A_server_open_to_the_network_with_an_allow_rule_for_the_current_network_is_allowed()
    {
        var verdict = NetworkAccessDecision.Judge([Allow(Private | Domain)], Private);

        Assert.Equal(FirewallVerdict.Allows, verdict);
    }

    [Fact]
    public void An_allow_rule_windows_created_itself_counts_the_same_as_weirs_own()
    {
        var verdict = NetworkAccessDecision.Judge([Allow(Public)], Public);

        Assert.Equal(FirewallVerdict.Allows, verdict);
    }

    [Fact]
    public void An_allow_rule_for_every_profile_allows_a_public_network()
    {
        var verdict = NetworkAccessDecision.Judge([Allow(Domain | Private | Public)], Public);

        Assert.Equal(FirewallVerdict.Allows, verdict);
    }

    [Fact]
    public void An_older_allow_rule_for_private_and_domain_only_blocks_a_public_network()
    {
        var verdict = NetworkAccessDecision.Judge([Allow(Domain | Private)], Public);

        Assert.Equal(FirewallVerdict.Blocks, verdict);
    }

    [Fact]
    public void A_block_rule_on_a_public_network_wins_over_an_allow_rule_for_every_profile()
    {
        var verdict = NetworkAccessDecision.Judge([Allow(Domain | Private | Public), Block(Public)], Public);

        Assert.Equal(FirewallVerdict.Blocks, verdict);
    }

    [Fact]
    public void A_block_rule_wins_over_an_allow_rule()
    {
        var verdict = NetworkAccessDecision.Judge([Allow(Private), Block(Private)], Private);

        Assert.Equal(FirewallVerdict.Blocks, verdict);
    }

    [Fact]
    public void A_block_rule_for_a_network_this_pc_is_not_on_changes_nothing()
    {
        var verdict = NetworkAccessDecision.Judge([Allow(Private), Block(Public)], Private);

        Assert.Equal(FirewallVerdict.Allows, verdict);
    }

    [Fact]
    public void A_server_open_to_the_network_with_no_rule_is_blocked()
    {
        var verdict = NetworkAccessDecision.Judge([], Private);

        Assert.Equal(FirewallVerdict.Blocks, verdict);
    }

    [Fact]
    public void A_disabled_allow_rule_lets_nobody_in()
    {
        var verdict = NetworkAccessDecision.Judge([Allow(Private, enabled: false)], Private);

        Assert.Equal(FirewallVerdict.Blocks, verdict);
    }

    [Fact]
    public void An_allow_rule_for_another_network_lets_nobody_in_on_this_one()
    {
        var verdict = NetworkAccessDecision.Judge([Allow(Private | Domain)], Public);

        Assert.Equal(FirewallVerdict.Blocks, verdict);
    }
}
