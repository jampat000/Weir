using Weir.Core.Time;

namespace Weir.Core.Tests.Metrics;

/// <summary>Where a person's day begins, which is what "today" on System counts from.</summary>
public sealed class TimeZoneDayTests
{
    [Fact]
    public void The_day_begins_at_local_midnight_in_the_named_zone()
    {
        var now = new DateTimeOffset(2026, 10, 2, 2, 30, 0, TimeSpan.Zero);

        // Tokyo is UTC+9: 02:30 UTC is 11:30 there, so its day began at 15:00 UTC the day before.
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero), TimeZones.StartOfLocalDay(now, "Asia/Tokyo").ToUniversalTime());
    }

    [Fact]
    public void An_unknown_or_missing_zone_means_utc()
    {
        var now = new DateTimeOffset(2026, 10, 2, 22, 15, 0, TimeSpan.Zero);
        var utcMidnight = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal((utcMidnight, utcMidnight), (TimeZones.StartOfLocalDay(now, null).ToUniversalTime(), TimeZones.StartOfLocalDay(now, "Not/AZone").ToUniversalTime()));
    }

    [Fact]
    public void A_zone_behind_utc_starts_its_day_later_in_utc_terms()
    {
        var now = new DateTimeOffset(2026, 10, 2, 3, 0, 0, TimeSpan.Zero);

        // 03:00 UTC is 23:00 on 1 October in New York (UTC-4).
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 4, 0, 0, TimeSpan.Zero), TimeZones.StartOfLocalDay(now, "America/New_York").ToUniversalTime());
    }
}
