using Weir.Tray.Firewall;
using Weir.Tray.LanAccess;
using Xunit;

namespace Weir.Tray.Tests.Firewall;

/// <summary>
/// <c>--configure-firewall</c>, <c>--remove-firewall</c> and <c>--allow-lan</c> (#779's documented unattended
/// flag) are headless commands: they must recognise each other correctly, never touch the firewall without
/// administrator rights, and never differ when <c>--silent</c> is also present, because none of them ever shows
/// anything to begin with. <c>--allow-lan</c> also turns LAN access on, with or without administrator rights.
/// </summary>
public sealed class FirewallCommandTests
{
    private static Func<IFirewallPolicy> NeverOpen =>
        () => throw new Xunit.Sdk.XunitException("The firewall policy must not be touched without administrator rights.");

    private static void NeverSave(ListenScope scope) =>
        throw new Xunit.Sdk.XunitException($"LAN access must not be saved ({scope}) by this command.");

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
        var exitCode = FirewallCommand.Run(["--configure-firewall"], _ => { }, isElevated: () => false, NeverOpen, NeverSave);

        Assert.Equal(FirewallExitCode.NotElevated, exitCode);
    }

    [Fact]
    public void Remove_without_elevation_is_refused_and_never_touches_the_policy()
    {
        var exitCode = FirewallCommand.Run(["--remove-firewall"], _ => { }, isElevated: () => false, NeverOpen, NeverSave);

        Assert.Equal(FirewallExitCode.NotElevated, exitCode);
    }

    [Fact]
    public void Silent_does_not_change_whether_elevation_is_required()
    {
        var exitCode = FirewallCommand.Run(["--configure-firewall", "--silent"], _ => { }, isElevated: () => false, NeverOpen, NeverSave);

        Assert.Equal(FirewallExitCode.NotElevated, exitCode);
    }

    // -- --allow-lan without administrator rights: access on, rule still missing ----

    [Fact]
    public void Allow_lan_without_elevation_turns_access_on_and_succeeds_without_touching_the_firewall()
    {
        var saved = new List<ListenScope>();

        var exitCode = FirewallCommand.Run(["--allow-lan"], _ => { }, isElevated: () => false, NeverOpen, saved.Add);

        Assert.Equal(FirewallExitCode.Success, exitCode);
        Assert.Equal([ListenScope.OtherDevices], saved);
    }

    [Fact]
    public void Allow_lan_without_elevation_says_in_the_log_that_the_rule_was_not_created()
    {
        var messages = new List<string>();

        FirewallCommand.Run(["--allow-lan"], messages.Add, isElevated: () => false, NeverOpen, _ => { });

        Assert.Contains(messages, m => m.Contains("firewall rule was not created", StringComparison.OrdinalIgnoreCase)
            && m.Contains("administrator", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Allow_lan_with_silent_still_turns_access_on_and_never_prompts()
    {
        var saved = new List<ListenScope>();

        var exitCode = FirewallCommand.Run(["--allow-lan", "--silent"], _ => { }, isElevated: () => false, NeverOpen, saved.Add);

        Assert.Equal(FirewallExitCode.Success, exitCode);
        Assert.Equal([ListenScope.OtherDevices], saved);
    }

    [Fact]
    public void Allow_lan_that_cannot_save_the_choice_fails_and_leaves_the_firewall_alone()
    {
        var messages = new List<string>();
        void Unwritable(ListenScope scope) => throw new UnauthorizedAccessException("Access to the path is denied.");

        var exitCode = FirewallCommand.Run(["--allow-lan"], messages.Add, isElevated: () => true, NeverOpen, Unwritable);

        Assert.Equal(FirewallExitCode.LanAccessNotSaved, exitCode);
        Assert.Contains(messages, m => m.Contains("LAN access", StringComparison.Ordinal));
    }

    // -- Elevated: the rule is actually written -------------------------------

    [Fact]
    public void Configure_when_elevated_writes_the_allow_rule_and_leaves_lan_access_alone()
    {
        var policy = new FakeFirewallPolicy();

        var exitCode = FirewallCommand.Run(["--configure-firewall"], _ => { }, isElevated: () => true, () => policy, NeverSave);

        Assert.Equal(FirewallExitCode.Success, exitCode);
        Assert.Contains(policy.Rules, r => r.Name == "Weir");
    }

    [Fact]
    public void Configure_logs_that_the_rule_covers_every_profile_and_widens_an_older_one()
    {
        var older = new FirewallRule("Weir", @"C:oldserverWeirServer.exe", FirewallRuleAction.Allow, FirewallRuleDirection.Inbound, FirewallProfiles.Domain | FirewallProfiles.Private, Enabled: true);
        var policy = new FakeFirewallPolicy(older);
        var messages = new List<string>();

        FirewallCommand.Run(["--configure-firewall"], messages.Add, isElevated: () => true, () => policy, NeverSave);

        Assert.Contains(messages, m => m.StartsWith("Firewall rule 'Weir' is in place (Domain, Private, Public profiles).", StringComparison.Ordinal));
        Assert.Equal(FirewallProfiles.Domain | FirewallProfiles.Private | FirewallProfiles.Public, Assert.Single(policy.Rules).Profiles);
    }

    [Fact]
    public void Allow_lan_when_elevated_turns_access_on_and_writes_the_rule_exactly_like_configure_firewall()
    {
        var policy = new FakeFirewallPolicy();
        var saved = new List<ListenScope>();

        var exitCode = FirewallCommand.Run(["--allow-lan"], _ => { }, isElevated: () => true, () => policy, saved.Add);

        Assert.Equal(FirewallExitCode.Success, exitCode);
        Assert.Contains(policy.Rules, r => r.Name == "Weir");
        Assert.Equal([ListenScope.OtherDevices], saved);
    }

    [Fact]
    public void Allow_lan_when_elevated_keeps_access_on_and_reports_a_firewall_failure()
    {
        var saved = new List<ListenScope>();
        IFirewallPolicy Throwing() => throw new InvalidOperationException("Windows Firewall's COM object is not registered on this machine.");

        var exitCode = FirewallCommand.Run(["--allow-lan"], _ => { }, isElevated: () => true, Throwing, saved.Add);

        Assert.Equal(FirewallExitCode.FirewallApiError, exitCode);
        Assert.Equal([ListenScope.OtherDevices], saved);
    }

    [Fact]
    public void Remove_when_elevated_deletes_the_allow_rule()
    {
        var policy = new FakeFirewallPolicy(new FirewallRule("Weir", @"C:\install\server\WeirServer.exe", FirewallRuleAction.Allow, FirewallRuleDirection.Inbound, FirewallProfiles.Private, Enabled: true));

        var exitCode = FirewallCommand.Run(["--remove-firewall"], _ => { }, isElevated: () => true, () => policy, NeverSave);

        Assert.Equal(FirewallExitCode.Success, exitCode);
        Assert.DoesNotContain(policy.Rules, r => r.Name == "Weir");
    }

    [Fact]
    public void A_firewall_api_failure_is_reported_with_its_own_exit_code_and_logged()
    {
        var messages = new List<string>();
        IFirewallPolicy Throwing() => throw new InvalidOperationException("Windows Firewall's COM object is not registered on this machine.");

        var exitCode = FirewallCommand.Run(["--configure-firewall"], messages.Add, isElevated: () => true, Throwing, NeverSave);

        Assert.Equal(FirewallExitCode.FirewallApiError, exitCode);
        Assert.Contains(messages, m => m.Contains("configure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Not_being_elevated_is_logged_in_plain_language()
    {
        var messages = new List<string>();

        FirewallCommand.Run(["--configure-firewall"], messages.Add, isElevated: () => false, NeverOpen, NeverSave);

        Assert.Contains(messages, m => m.Contains("administrator", StringComparison.OrdinalIgnoreCase));
    }
}
