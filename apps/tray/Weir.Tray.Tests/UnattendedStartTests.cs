using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// <c>--silent</c> is what a program driving Weir unattended (an installer, a provisioning script) passes: no
/// browser, no dialog, no message box, regardless of what the desktop heuristic reports. These pin that override
/// and that a supplied port is still honoured while silent (#779).
/// </summary>
public sealed class UnattendedStartTests
{
    [Fact]
    public void No_arguments_are_not_silent()
    {
        Assert.False(Program.IsSilent([]));
    }

    [Fact]
    public void The_silent_argument_is_recognised()
    {
        Assert.True(Program.IsSilent(["--silent"]));
    }

    [Fact]
    public void Silent_combines_with_other_arguments()
    {
        Assert.True(Program.IsSilent(["--port", "9400", "--silent"]));
    }

    [Fact]
    public void Silent_does_not_announce_itself()
    {
        Assert.False(Program.AnnouncesStart(["--silent"]));
    }

    [Fact]
    public void Silent_with_a_port_still_does_not_announce_itself()
    {
        Assert.False(Program.AnnouncesStart(["--port", "9400", "--silent"]));
    }

    [Fact]
    public void Without_silent_the_start_is_announced_by_default()
    {
        Assert.True(Program.AnnouncesStart(["--port", "9400"]));
    }

    [Fact]
    public void Silent_overrides_a_desktop_the_heuristic_reports_as_interactive()
    {
        Assert.False(Program.HasInteractiveDesktop(["--silent"], () => true));
    }

    [Fact]
    public void Without_silent_the_desktop_heuristic_is_used_as_is()
    {
        Assert.True(Program.HasInteractiveDesktop([], () => true));
        Assert.False(Program.HasInteractiveDesktop([], () => false));
    }

    [Fact]
    public void Silent_never_asks_the_desktop_heuristic()
    {
        Assert.False(Program.HasInteractiveDesktop(["--silent"], () => throw new Xunit.Sdk.XunitException("Silent must not consult the desktop heuristic at all.")));
    }

    // -- The first-run firewall prompt: the same rule, belt and suspenders ---

    [Fact]
    public void The_first_run_firewall_prompt_never_shows_during_a_silent_start_even_on_a_desktop()
    {
        Assert.False(Program.ShouldPromptForFirewallAccess(["--silent"], () => true));
    }

    [Fact]
    public void The_first_run_firewall_prompt_never_shows_without_an_interactive_desktop()
    {
        Assert.False(Program.ShouldPromptForFirewallAccess([], () => false));
    }

    [Fact]
    public void The_first_run_firewall_prompt_can_show_on_an_interactive_desktop()
    {
        Assert.True(Program.ShouldPromptForFirewallAccess([], () => true));
    }

    [Fact]
    public void The_first_run_firewall_prompt_never_consults_the_desktop_heuristic_when_silent()
    {
        Assert.False(Program.ShouldPromptForFirewallAccess(
            ["--silent"],
            () => throw new Xunit.Sdk.XunitException("A silent start must never consult the desktop heuristic at all.")));
    }

    [Fact]
    public void A_silent_start_with_a_supplied_port_is_used_without_asking_even_though_a_desktop_is_reported()
    {
        string[] args = ["--silent", "--port", "9400"];
        var supplied = PortChoice.SuppliedPort(args, _ => null);
        var interactive = Program.HasInteractiveDesktop(args, () => true);

        var decision = PortChoice.Decide(
            supplied,
            saved: null,
            interactive,
            isInUse: _ => false,
            ask: _ => throw new Xunit.Sdk.XunitException("A silent start must never show the port dialog."));

        Assert.Equal(9400, decision.Port);
        Assert.True(decision.Save);
    }
}
