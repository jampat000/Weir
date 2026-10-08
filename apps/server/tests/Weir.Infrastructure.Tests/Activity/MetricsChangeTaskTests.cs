using Microsoft.Extensions.Time.Testing;
using Weir.Core.Metrics;
using Weir.Infrastructure.Activity;

namespace Weir.Infrastructure.Tests.Activity;

/// <summary>Open screens hear that Weir's counters moved once per tick, and only while a stream is open to hear it.</summary>
public sealed class MetricsChangeTaskTests
{
    private readonly RuntimeMetricsStore _metrics = new(new FakeTimeProvider());
    private readonly DataChangePublisher _changes = new();
    private readonly MetricsChangeTask _task;

    public MetricsChangeTaskTests() => _task = new MetricsChangeTask(_metrics, _changes);

    private static async Task<string[]> HeardAsync(BroadcastSubscription<string> heard)
    {
        heard.Dispose();
        var topics = new List<string>();
        await foreach (var topic in heard.ReadAllAsync(CancellationToken.None))
        {
            topics.Add(topic);
        }

        return [.. topics];
    }

    [Fact]
    public async Task Many_requests_between_two_ticks_are_announced_once()
    {
        using var heard = _changes.Subscribe();
        for (var request = 0; request < 20; request++)
        {
            _metrics.RecordRequest("GET", "/processing/jobs", 200, 3);
        }

        await _task.RunOnceAsync(CancellationToken.None);
        await _task.RunOnceAsync(CancellationToken.None);

        Assert.Equal([DataTopics.Metrics], await HeardAsync(heard));
    }

    [Fact]
    public async Task Nothing_is_announced_while_the_counters_stand_still()
    {
        using var heard = _changes.Subscribe();

        await _task.RunOnceAsync(CancellationToken.None);

        Assert.Empty(await HeardAsync(heard));
    }

    [Fact]
    public async Task A_screen_reading_the_counters_does_not_make_them_move_again()
    {
        using var heard = _changes.Subscribe();
        _metrics.RecordRequest("GET", "/processing/jobs", 200, 3);
        await _task.RunOnceAsync(CancellationToken.None);

        _metrics.RecordRequest("GET", "/suite/metrics", 200, 3);
        await _task.RunOnceAsync(CancellationToken.None);

        Assert.Equal([DataTopics.Metrics], await HeardAsync(heard));
    }

    [Fact]
    public async Task A_stream_that_opens_after_the_counters_moved_is_told_on_the_next_tick()
    {
        _metrics.RecordRequest("GET", "/processing/jobs", 200, 3);
        await _task.RunOnceAsync(CancellationToken.None);

        using var heard = _changes.Subscribe();
        await _task.RunOnceAsync(CancellationToken.None);

        Assert.Equal([DataTopics.Metrics], await HeardAsync(heard));
    }
}
