using Microsoft.Extensions.Time.Testing;
using Weir.Core.Metrics;

namespace Weir.Core.Tests.Metrics;

/// <summary>What tells a screen showing Weir's counters that they moved, without moving itself when the screen reads them.</summary>
public sealed class ChangeStampTests
{
    private readonly RuntimeMetricsStore _store = new(new FakeTimeProvider(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void An_answered_request_moves_the_stamp()
    {
        var before = _store.ChangeStamp();

        _store.RecordRequest("GET", "/processing/jobs", 200, 4);

        Assert.NotEqual(before, _store.ChangeStamp());
    }

    [Fact]
    public void Reading_the_counters_does_not_move_it()
    {
        var before = _store.ChangeStamp();

        _store.RecordRequest("GET", "/suite/metrics", 200, 4);
        _store.RecordRequest("GET", "/suite/metrics", 200, 4);

        Assert.Equal(before, _store.ChangeStamp());
    }

    [Fact]
    public void Other_methods_of_the_same_route_do_move_it()
    {
        var before = _store.ChangeStamp();

        _store.RecordRequest("POST", "/suite/metrics", 405, 1);

        Assert.NotEqual(before, _store.ChangeStamp());
    }

    [Theory]
    [InlineData("ERROR")]
    [InlineData("CRITICAL")]
    public void A_logged_failure_moves_it(string level)
    {
        var before = _store.ChangeStamp();

        _store.RecordLog(level);

        Assert.NotEqual(before, _store.ChangeStamp());
    }

    [Fact]
    public void A_routine_log_line_does_not()
    {
        var before = _store.ChangeStamp();

        _store.RecordLog("INFO");

        Assert.Equal(before, _store.ChangeStamp());
    }
}
