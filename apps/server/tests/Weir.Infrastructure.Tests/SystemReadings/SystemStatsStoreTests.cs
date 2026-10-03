using Microsoft.Extensions.Time.Testing;
using Weir.Infrastructure.SystemReadings;

namespace Weir.Infrastructure.Tests.SystemReadings;

public sealed class SystemStatsStoreTests
{
    private const int Cores = 8;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

    private StatsSample Sample(double cpu) => StatsSample.Of(StatsNow.Unread(_time.GetUtcNow(), Cores) with { CpuPercent = cpu });

    private SystemStatsStore Store() => new(_time, Cores);

    [Fact]
    public void Before_any_reading_the_snapshot_has_only_the_core_count()
    {
        var snapshot = Store().Snapshot();

        Assert.Equal(StatsNow.Unread(_time.GetUtcNow(), Cores), snapshot.Now);
        Assert.Empty(snapshot.History);
    }

    [Fact]
    public void The_snapshot_has_the_newest_reading_and_every_point_oldest_first()
    {
        var store = Store();
        store.Add(Sample(10));
        _time.Advance(TimeSpan.FromSeconds(1));
        store.Add(Sample(20));

        var snapshot = store.Snapshot();

        Assert.Equal(20, snapshot.Now.CpuPercent);
        Assert.Equal([10.0, 20.0], snapshot.History.Select(point => point.CpuPercent));
    }

    [Fact]
    public void No_more_than_the_capacity_is_kept_and_the_oldest_go_first()
    {
        var store = Store();
        for (var i = 0; i < SystemStatsStore.Capacity + 5; i++)
        {
            store.Add(Sample(i));
        }

        var history = store.Snapshot().History;

        Assert.Equal(SystemStatsStore.Capacity, history.Count);
        Assert.Equal(5, history[0].CpuPercent);
    }

    [Fact]
    public void The_history_reaches_back_only_as_far_as_the_window()
    {
        var store = Store();
        store.Add(Sample(1));
        _time.Advance(SystemStatsStore.Window + TimeSpan.FromSeconds(1));
        store.Add(Sample(2));

        Assert.Equal([2.0], store.Snapshot().History.Select(point => point.CpuPercent));
    }

    [Fact]
    public void The_machine_and_drives_are_served_as_set()
    {
        var store = Store();
        var drive = new DriveReading("D:", "D:\\", 10, 5, 1, 2, null, null, null, null, []);
        store.SetMachine(new MachineFacts("Test OS", 99, false));
        store.SetDrives([drive]);

        var snapshot = store.Snapshot();

        Assert.Equal(new MachineFacts("Test OS", 99, false), snapshot.Machine);
        Assert.Equal([drive], snapshot.Drives);
    }

    [Fact]
    public async Task A_stream_asking_for_what_there_is_gets_the_newest_reading_at_once()
    {
        var store = Store();
        store.Add(Sample(10));
        store.Add(Sample(20));

        var update = await store.NextAfterAsync(0, CancellationToken.None);

        Assert.Equal(20, update.Sample.Now.CpuPercent);
        Assert.Equal(2, update.Version);
    }

    [Fact]
    public async Task A_stream_that_has_seen_the_newest_reading_waits_for_the_next()
    {
        var store = Store();
        store.Add(Sample(10));
        var seen = (await store.NextAfterAsync(0, CancellationToken.None)).Version;

        var next = store.NextAfterAsync(seen, CancellationToken.None);
        Assert.False(next.IsCompleted);
        store.Add(Sample(30));

        Assert.Equal(30, (await next).Sample.Now.CpuPercent);
    }

    [Fact]
    public async Task A_stream_waiting_for_a_reading_stops_waiting_when_it_is_cancelled()
    {
        using var cancel = new CancellationTokenSource();
        var waiting = Store().NextAfterAsync(0, cancel.Token);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }
}
