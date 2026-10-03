using Microsoft.Extensions.Time.Testing;
using Weir.Infrastructure.SystemReadings;

namespace Weir.Infrastructure.Tests.SystemReadings;

public sealed class FreeSpaceForecastTests
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

    /// <summary>Reads every ten minutes for <paramref name="hours"/>, losing <paramref name="lostPerHour"/> each hour.</summary>
    private double? FillFor(FreeSpaceForecast forecast, int hours, long startFree, long lostPerHour)
    {
        double? days = null;
        for (var minutes = 0; minutes <= hours * 60; minutes += 10)
        {
            days = forecast.Record("D:", startFree - lostPerHour * minutes / 60);
            _time.Advance(TimeSpan.FromMinutes(10));
        }

        return days;
    }

    [Fact]
    public void A_drive_losing_space_steadily_is_full_after_its_free_space_divided_by_the_pace()
    {
        var forecast = new FreeSpaceForecast(_time);

        // After two hours of losing 10 GB an hour, 480 GB are left: 48 hours more at that pace.
        var days = FillFor(forecast, hours: 2, startFree: 500 * Gigabyte, lostPerHour: 10 * Gigabyte);

        Assert.Equal(2.0, days);
    }

    [Fact]
    public void A_drive_that_is_not_filling_has_no_forecast()
    {
        var forecast = new FreeSpaceForecast(_time);

        Assert.Null(FillFor(forecast, hours: 3, startFree: 500 * Gigabyte, lostPerHour: 0));
        Assert.Null(FillFor(new FreeSpaceForecast(_time), hours: 3, startFree: 500 * Gigabyte, lostPerHour: -5 * Gigabyte));
    }

    [Fact]
    public void Less_than_an_hour_of_history_is_too_little_to_forecast_from()
    {
        var forecast = new FreeSpaceForecast(_time);

        Assert.Null(FillFor(forecast, hours: 0, startFree: 500 * Gigabyte, lostPerHour: 100 * Gigabyte));
        Assert.Null(forecast.Record("D:", 400 * Gigabyte));
    }

    [Fact]
    public void A_drive_that_would_take_over_a_year_to_fill_has_no_forecast()
    {
        var forecast = new FreeSpaceForecast(_time);

        Assert.Null(FillFor(forecast, hours: 3, startFree: 500 * Gigabyte, lostPerHour: 1024 * 1024));
    }

    [Fact]
    public void Each_drive_is_forecast_from_its_own_readings()
    {
        var forecast = new FreeSpaceForecast(_time);
        for (var minutes = 0; minutes <= 120; minutes += 10)
        {
            forecast.Record("D:", 500 * Gigabyte - 10 * Gigabyte * minutes / 60);
            Assert.Null(forecast.Record("E:", 500 * Gigabyte));
            _time.Advance(TimeSpan.FromMinutes(10));
        }

        Assert.NotNull(forecast.Record("D:", 480 * Gigabyte));
    }
}
