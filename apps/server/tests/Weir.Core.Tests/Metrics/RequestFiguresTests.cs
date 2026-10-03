using Microsoft.Extensions.Time.Testing;
using Weir.Core.Metrics;

namespace Weir.Core.Tests.Metrics;

/// <summary>How fast Weir answers and how often it fails, over a recent window and over "today".</summary>
public sealed class RequestFiguresTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Noon);
    private readonly RuntimeMetricsStore _store;

    public RequestFiguresTests() => _store = new RuntimeMetricsStore(_time);

    private void Answer(double milliseconds, int status = 200) => _store.RecordRequest("GET", "/api/v1/test", status, milliseconds);

    [Fact]
    public void With_no_requests_every_figure_is_zero()
    {
        Assert.Equal(new RuntimeMetricsStore.RequestFigures(0, 0, 0), _store.GetRequestFigures(Noon.AddHours(-12)));
    }

    [Fact]
    public void The_median_and_the_slowest_twentieth_come_from_the_recent_answers()
    {
        for (var milliseconds = 1; milliseconds <= 20; milliseconds++)
        {
            Answer(milliseconds);
        }

        var figures = _store.GetRequestFigures(Noon.AddHours(-12));

        Assert.Equal((10.0, 19.0), (figures.MedianMs, figures.P95Ms));
    }

    [Fact]
    public void An_answer_older_than_the_window_no_longer_counts()
    {
        Answer(900);
        _time.Advance(RuntimeMetricsStore.TimingWindow + TimeSpan.FromSeconds(1));
        Answer(5);

        var figures = _store.GetRequestFigures(Noon.AddHours(-12));

        Assert.Equal((5.0, 5.0), (figures.MedianMs, figures.P95Ms));
    }

    [Fact]
    public void A_request_that_held_its_connection_open_is_counted_but_not_timed()
    {
        Answer(10);
        _store.RecordStreamRequest("GET", "/api/v1/activity/stream", 200, 3_600_000);

        var figures = _store.GetRequestFigures(Noon.AddHours(-12));

        Assert.Equal((10.0, 10.0), (figures.MedianMs, figures.P95Ms));
        Assert.Equal(2, _store.GetSummary().TotalRequests);
    }

    [Fact]
    public void Only_server_errors_since_the_given_time_are_counted_as_errors()
    {
        Answer(5, status: 404);
        Answer(5, status: 500);
        _time.Advance(TimeSpan.FromHours(2));
        var since = _time.GetUtcNow();
        Answer(5, status: 503);
        Answer(5, status: 200);
        Answer(5, status: 500);

        Assert.Equal(2, _store.GetRequestFigures(since).ServerErrors);
        Assert.Equal(3, _store.GetRequestFigures(Noon).ServerErrors);
    }

    [Fact]
    public void A_server_error_is_forgotten_after_two_days()
    {
        Answer(5, status: 500);
        _time.Advance(TimeSpan.FromDays(2) + TimeSpan.FromMinutes(1));

        Assert.Equal(0, _store.GetRequestFigures(Noon).ServerErrors);
    }
}
