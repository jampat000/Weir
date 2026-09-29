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
    public void A_server_that_listens_on_this_pc_only_reports_this_pc_only_whatever_the_firewall_says()
    {
        var state = NetworkAccessDecision.Decide(listensOnThisPcOnly: true, [Block(Private)], Private);

        Assert.Equal(NetworkAccessState.ThisPcOnly, state);
    }

    [Fact]
    public void A_server_open_to_the_network_with_an_allow_rule_for_the_current_network_is_allowed()
    {
        var state = NetworkAccessDecision.Decide(listensOnThisPcOnly: false, [Allow(Private | Domain)], Private);

        Assert.Equal(NetworkAccessState.Allowed, state);
    }

    [Fact]
    public void An_allow_rule_windows_created_itself_counts_the_same_as_weirs_own()
    {
        var state = NetworkAccessDecision.Decide(listensOnThisPcOnly: false, [Allow(Public)], Public);

        Assert.Equal(NetworkAccessState.Allowed, state);
    }

    [Fact]
    public void A_block_rule_wins_over_an_allow_rule()
    {
        var state = NetworkAccessDecision.Decide(listensOnThisPcOnly: false, [Allow(Private), Block(Private)], Private);

        Assert.Equal(NetworkAccessState.Blocked, state);
    }

    [Fact]
    public void A_block_rule_for_a_network_this_pc_is_not_on_changes_nothing()
    {
        var state = NetworkAccessDecision.Decide(listensOnThisPcOnly: false, [Allow(Private), Block(Public)], Private);

        Assert.Equal(NetworkAccessState.Allowed, state);
    }

    [Fact]
    public void A_server_open_to_the_network_with_no_rule_is_blocked()
    {
        var state = NetworkAccessDecision.Decide(listensOnThisPcOnly: false, [], Private);

        Assert.Equal(NetworkAccessState.Blocked, state);
    }

    [Fact]
    public void A_disabled_allow_rule_lets_nobody_in()
    {
        var state = NetworkAccessDecision.Decide(listensOnThisPcOnly: false, [Allow(Private, enabled: false)], Private);

        Assert.Equal(NetworkAccessState.Blocked, state);
    }

    [Fact]
    public void An_allow_rule_for_another_network_lets_nobody_in_on_this_one()
    {
        var state = NetworkAccessDecision.Decide(listensOnThisPcOnly: false, [Allow(Private | Domain)], Public);

        Assert.Equal(NetworkAccessState.Blocked, state);
    }
}
