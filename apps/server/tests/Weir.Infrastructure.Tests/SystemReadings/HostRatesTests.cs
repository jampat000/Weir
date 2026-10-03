using Weir.Infrastructure.SystemReadings;

namespace Weir.Infrastructure.Tests.SystemReadings;

public sealed class HostRatesTests
{
    [Fact]
    public void Processor_use_is_the_share_of_the_window_that_was_not_idle()
    {
        Assert.Equal(25.0, HostRates.CpuPercent(new CpuTimes(0, 0), new CpuTimes(750, 1000)));
    }

    [Fact]
    public void Processor_use_is_unknown_when_the_counters_did_not_move_or_went_backwards()
    {
        Assert.Null(HostRates.CpuPercent(new CpuTimes(10, 100), new CpuTimes(10, 100)));
        Assert.Null(HostRates.CpuPercent(new CpuTimes(10, 100), new CpuTimes(5, 200)));
    }

    [Fact]
    public void A_rate_is_bytes_gained_over_the_seconds_that_passed()
    {
        Assert.Equal(500, HostRates.PerSecond(1000, 6000, 10));
    }

    [Fact]
    public void A_counter_that_went_backwards_has_no_rate()
    {
        Assert.Null(HostRates.PerSecond(6000, 1000, 10));
        Assert.Null(HostRates.PerSecond(1000, 2000, 0));
    }

    [Fact]
    public void A_disk_is_busy_for_the_part_of_the_window_it_was_not_idle()
    {
        var before = new DiskCounters(0, 0, IdleTime: 0, QueryTime: 0);
        var now = new DiskCounters(0, 0, IdleTime: 700, QueryTime: 1000);

        Assert.Equal(30.0, HostRates.BusyPercent(before, now));
    }

    [Fact]
    public void Disk_activity_combines_throughput_and_busy_share()
    {
        var before = new DiskCounters(1000, 2000, 0, 0);
        var now = new DiskCounters(11_000, 7000, 500, 1000);

        var activity = HostRates.DiskActivityBetween(before, now, seconds: 10);

        Assert.Equal(new DiskActivity(1000, 500, 50.0), activity);
    }

    [Fact]
    public void Disk_activity_is_unknown_without_both_readings()
    {
        Assert.Null(HostRates.DiskActivityBetween(null, new DiskCounters(1, 1, 1, 1), 10));
        Assert.Null(HostRates.DiskActivityBetween(new DiskCounters(1, 1, 1, 1), null, 10));
    }
}
