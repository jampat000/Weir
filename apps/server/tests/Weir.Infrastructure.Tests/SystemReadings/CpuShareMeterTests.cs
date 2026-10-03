using Microsoft.Extensions.Time.Testing;
using Weir.Infrastructure.SystemReadings;

namespace Weir.Infrastructure.Tests.SystemReadings;

public sealed class CpuShareMeterTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void The_share_is_processor_time_used_against_all_the_cores_over_the_window()
    {
        var meter = new CpuShareMeter(_time, cores: 4);
        Assert.Null(meter.Read(TimeSpan.FromSeconds(10)));

        _time.Advance(TimeSpan.FromSeconds(10));

        // Ten seconds of a four-core machine is forty seconds of processor; ten were used.
        Assert.Equal(25.0, meter.Read(TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void A_burst_across_more_cores_than_there_are_is_capped_at_the_whole_machine()
    {
        var meter = new CpuShareMeter(_time, cores: 1);
        meter.Read(TimeSpan.Zero);
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(100.0, meter.Read(TimeSpan.FromSeconds(3)));
    }
}
