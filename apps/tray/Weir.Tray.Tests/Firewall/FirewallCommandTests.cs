using Weir.Tray.Firewall;
using Xunit;

namespace Weir.Tray.Tests.Firewall;

/// <summary>
/// <c>--configure-firewall</c>, <c>--remove-firewall</c> and <c>--allow-lan</c> (#779's documented unattended
/// flag) are headless commands: they must recognise each other correctly, never touch the firewall without
/// administrator rights, and never differ when <c>--silent</c> is also present, because none of them ever shows
/// anything to begin with.
/// </summary>
public sealed class FirewallCommandTests
{
    private static Func<IFirewallPolicy> NeverOpen =>
        () => throw new Xunit.Sdk.XunitException("The firewall policy must not be touched without administrator rights.");

    // -- Recognising the arguments -------------------------------------------

    [Fact]
    public void Handles_recognises_configure_firewall()
    {
        Assert.True(FirewallCommand.Handles(["--configure-firewall"]));
    }

    [Fact]
    public void Handles_recognises_remove_firewall()
    {
        Assert.True(FirewallCommand.Handles(["--remove-firewall"]));
    }

    [Fact]
    public void Handles_recognises_allow_lan()
    {
        Assert.True(FirewallCommand.Handles(["--allow-lan"]));
    }

    [Fact]
    public void Handles_recognises_a_firewall_argument_alongside_others()
    {
        Assert.True(FirewallCommand.Handles(["--port", "9400", "--configure-firewall"]));
    }

    [Fact]
    public void Handles_ignores_a_start_with_no_arguments()
    {
        Assert.False(FirewallCommand.Handles([]));
    }

    [Fact]
    public void Handles_ignores_unrelated_arguments()
    {
        Assert.False(FirewallCommand.Handles(["--port", "9400"]));
    }

    [Fact]
    public void Handles_ignores_silent_on_its_own()
    {
        Assert.False(FirewallCommand.Handles(["--silent"]));
    }

    // -- Elevation is required, never assumed ---------------------------------

    [Fact]
    public void Configure_without_elevation_is_refused_and_never_touches_the_policy()
    {
        var exitCode = FirewallCommand.Run(["--configure-firewall"], _ => { }, isElevated: () => false, NeverOpen);

        Assert.Equal(FirewallExitCode.NotElevated, exitCode);
    }

    [Fact]
    public void Remove_without_elevation_is_refused_and_never_touches_the_policy()
    {
        var exitCode = FirewallCommand.Run(["--remove-firewall"], _ => { }, isElevated: () => false, NeverOpen);

        Assert.Equal(FirewallExitCode.NotElevated, exitCode);
    }

    [Fact]
    public void Allow_lan_without_elevation_is_refused_rather_than_prompting_or_hanging()
    {
        var exitCode = FirewallCommand.Run(["--allow-lan"], _ => { }, isElevated: () => false, NeverOpen);

        Assert.Equal(FirewallExitCode.NotElevated, exitCode);
    }

    [Fact]
    public void Silent_does_not_change_whether_elevation_is_required()
    {
        var exitCode = FirewallCommand.Run(["--allow-lan", "--silent"], _ => { }, isElevated: () => false, NeverOpen);

        Assert.Equal(FirewallExitCode.NotElevated, exitCode);
    }

    // -- Elevated: the rule is actually written -------------------------------

    [Fact]
    public void Configure_when_elevated_writes_the_allow_rule()
    {
        var policy = new FakeFirewallPolicy();

        var exitCode = FirewallCommand.Run(["--configure-firewall"], _ => { }, isElevated: () => true, () => policy);

        Assert.Equal(FirewallExitCode.Success, exitCode);
        Assert.Contains(policy.Rules, r => r.Name == "Weir");
    }

    [Fact]
    public void Allow_lan_when_elevated_configures_the_rule_exactly_like_configure_firewall()
    {
        var policy = new FakeFirewallPolicy();

        var exitCode = FirewallCommand.Run(["--allow-lan"], _ => { }, isElevated: () => true, () => policy);

        Assert.Equal(FirewallExitCode.Success, exitCode);
        Assert.Contains(policy.Rules, r => r.Name == "Weir");
    }

    [Fact]
    public void Remove_when_elevated_deletes_the_allow_rule()
    {
        var policy = new FakeFirewallPolicy(new FirewallRule("Weir", @"C:\install\server\WeirServer.exe", FirewallRuleAction.Allow, FirewallRuleDirection.Inbound, FirewallProfiles.Private, Enabled: true));

        var exitCode = FirewallCommand.Run(["--remove-firewall"], _ => { }, isElevated: () => true, () => policy);

        Assert.Equal(FirewallExitCode.Success, exitCode);
        Assert.DoesNotContain(policy.Rules, r => r.Name == "Weir");
    }

    [Fact]
    public void A_firewall_api_failure_is_reported_with_its_own_exit_code_and_logged()
    {
        var messages = new List<string>();
        IFirewallPolicy Throwing() => throw new InvalidOperationException("Windows Firewall's COM object is not registered on this machine.");

        var exitCode = FirewallCommand.Run(["--configure-firewall"], messages.Add, isElevated: () => true, Throwing);

        Assert.Equal(FirewallExitCode.FirewallApiError, exitCode);
        Assert.Contains(messages, m => m.Contains("configure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Not_being_elevated_is_logged_in_plain_language()
    {
        var messages = new List<string>();

        FirewallCommand.Run(["--configure-firewall"], messages.Add, isElevated: () => false, NeverOpen);

        Assert.Contains(messages, m => m.Contains("administrator", StringComparison.OrdinalIgnoreCase));
    }
}
