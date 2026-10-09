using Xunit;

namespace Weir.Tray.Tests;

/// <summary>The starting dot blinks until the tray can say green, amber or red, and not a moment longer.</summary>
public sealed class TrayBlinkTests
{
    [Fact]
    public void Nothing_blinks_until_the_dot_is_starting()
    {
        var blink = new TrayBlink();

        Assert.False(blink.Active);
        Assert.False(blink.Tick());
        Assert.True(blink.Lit);
    }

    [Fact]
    public void A_starting_dot_blinks_lit_then_unlit_by_turns()
    {
        var blink = new TrayBlink();
        blink.Follow(TrayDot.Starting);

        Assert.True(blink.Active);
        Assert.True(blink.Lit);
        Assert.True(blink.Tick());
        Assert.False(blink.Lit);
        Assert.True(blink.Tick());
        Assert.True(blink.Lit);
    }

    [Fact]
    public void The_first_known_state_stops_the_blink_with_the_dot_lit()
    {
        foreach (var known in new[] { TrayDot.Green, TrayDot.Amber, TrayDot.Red })
        {
            var blink = new TrayBlink();
            blink.Follow(TrayDot.Starting);
            blink.Tick();
            Assert.False(blink.Lit);

            blink.Follow(known);

            Assert.False(blink.Active, known.ToString());
            Assert.True(blink.Lit, known.ToString());
            Assert.False(blink.Tick(), known.ToString());
            Assert.True(blink.Lit, known.ToString());
        }
    }

    [Fact]
    public void Being_told_again_that_it_is_starting_does_not_restart_the_blink()
    {
        var blink = new TrayBlink();
        blink.Follow(TrayDot.Starting);
        blink.Tick();

        blink.Follow(TrayDot.Starting);

        Assert.False(blink.Lit);
    }

    [Fact]
    public void A_server_that_starts_again_blinks_again()
    {
        var blink = new TrayBlink();
        blink.Follow(TrayDot.Starting);
        blink.Follow(TrayDot.Green);

        blink.Follow(TrayDot.Starting);

        Assert.True(blink.Active);
        Assert.True(blink.Tick());
    }

    [Fact]
    public void A_state_that_blinks_shows_the_dot_and_then_nothing_in_its_place()
    {
        var blink = new TrayBlink();
        var starting = new TrayState(ServerPhase.Starting, null, null, 9347);
        blink.Follow(starting.Dot);

        var lit = starting.IconKey(blink.Lit);
        blink.Tick();
        var unlit = starting.IconKey(blink.Lit);

        Assert.Equal(TrayDot.Starting, lit.Dot);
        Assert.Null(unlit.Dot);
    }
}
