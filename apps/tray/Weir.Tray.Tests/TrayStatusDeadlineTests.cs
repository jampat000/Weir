using Xunit;

namespace Weir.Tray.Tests;

/// <summary>A running server that never gives a readable status stops blinking after 30 seconds.</summary>
public sealed class TrayStatusDeadlineTests
{
    private DateTimeOffset _now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private TrayStatusDeadline Deadline() => new(() => _now);

    [Fact]
    public void Nothing_is_overdue_before_a_wait_begins()
    {
        var deadline = Deadline();
        _now += TimeSpan.FromMinutes(5);

        Assert.False(deadline.Overdue);
    }

    [Fact]
    public void A_wait_is_overdue_after_thirty_seconds_and_not_before()
    {
        var deadline = Deadline();
        deadline.Follow(waiting: true);

        _now += TrayStatusDeadline.Wait - TimeSpan.FromSeconds(1);
        Assert.False(deadline.Overdue);

        _now += TimeSpan.FromSeconds(1);
        Assert.True(deadline.Overdue);
    }

    [Fact]
    public void Being_told_again_that_it_is_waiting_does_not_restart_the_wait()
    {
        var deadline = Deadline();
        deadline.Follow(waiting: true);
        _now += TimeSpan.FromSeconds(20);

        deadline.Follow(waiting: true);
        _now += TimeSpan.FromSeconds(10);

        Assert.True(deadline.Overdue);
    }

    [Fact]
    public void A_status_or_a_server_that_is_not_running_ends_the_wait_and_the_next_one_starts_afresh()
    {
        var deadline = Deadline();
        deadline.Follow(waiting: true);
        _now += TrayStatusDeadline.Wait;
        Assert.True(deadline.Overdue);

        deadline.Follow(waiting: false);
        Assert.False(deadline.Overdue);

        deadline.Follow(waiting: true);
        Assert.False(deadline.Overdue);
        _now += TrayStatusDeadline.Wait;
        Assert.True(deadline.Overdue);
    }
}
