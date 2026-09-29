using Weir.Tray.LanAccess;
using Xunit;

namespace Weir.Tray.Tests.LanAccess;

/// <summary>The two tray items for LAN access, in each state the tray can be in.</summary>
public sealed class LanAccessMenuStateTests
{
    [Fact]
    public void When_only_this_pc_can_connect_the_person_can_allow_others_but_not_limit_further()
    {
        var state = LanAccessMenuState.Describe(ListenScope.ThisPcOnly, LanAccessActivity.Idle);

        Assert.Equal(new LanAccessMenuState("Allow other devices on your network...", true, "Only allow this PC", false), state);
    }

    [Fact]
    public void When_other_devices_can_connect_the_person_can_limit_it_and_can_still_repair_the_rule()
    {
        var state = LanAccessMenuState.Describe(ListenScope.OtherDevices, LanAccessActivity.Idle);

        Assert.Equal(new LanAccessMenuState("Allow other devices on your network...", true, "Only allow this PC", true), state);
    }

    [Theory]
    [InlineData((int)ListenScope.ThisPcOnly)]
    [InlineData((int)ListenScope.OtherDevices)]
    public void While_waiting_for_windows_admin_approval_both_items_are_disabled(int scope)
    {
        var state = LanAccessMenuState.Describe((ListenScope)scope, LanAccessActivity.WaitingForWindows);

        Assert.Equal("Waiting for Windows admin approval...", state.AllowText);
        Assert.False(state.AllowEnabled);
        Assert.False(state.ThisPcOnlyEnabled);
    }

    [Fact]
    public void While_restarting_for_other_devices_the_allow_item_says_so_and_both_are_disabled()
    {
        var state = LanAccessMenuState.Describe(ListenScope.ThisPcOnly, LanAccessActivity.RestartingForOtherDevices);

        Assert.Equal("Restarting Weir for other devices...", state.AllowText);
        Assert.False(state.AllowEnabled);
        Assert.False(state.ThisPcOnlyEnabled);
    }

    [Fact]
    public void While_restarting_for_this_pc_only_the_limit_item_says_so_and_both_are_disabled()
    {
        var state = LanAccessMenuState.Describe(ListenScope.OtherDevices, LanAccessActivity.RestartingForThisPcOnly);

        Assert.Equal("Restarting Weir for this PC only...", state.ThisPcOnlyText);
        Assert.False(state.AllowEnabled);
        Assert.False(state.ThisPcOnlyEnabled);
    }
}
