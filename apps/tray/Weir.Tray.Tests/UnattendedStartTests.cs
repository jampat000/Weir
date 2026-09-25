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
    public void Silent_does_not_open_the_browser()
    {
        Assert.False(Program.OpensBrowser(["--silent"]));
    }

    [Fact]
    public void Silent_with_a_port_still_does_not_open_the_browser()
    {
        Assert.False(Program.OpensBrowser(["--port", "9400", "--silent"]));
    }

    [Fact]
    public void Without_silent_the_browser_still_opens_by_default()
    {
        Assert.True(Program.OpensBrowser(["--port", "9400"]));
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
