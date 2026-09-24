using Weir.Infrastructure.Processing;

namespace Weir.Infrastructure.Tests.Processing;

/// <summary>
/// The process-wide, in-memory live progress store (#750): what a running pass looks like on the Processing screen,
/// kept up to date without ever touching the database.
/// </summary>
public sealed class LiveProgressStoreTests
{
    private static LiveProgress Writing(double percent) => new(
        Percent: percent,
        Message: "Weir is writing the cleaned-up file.",
        EtaSeconds: 31.0,
        Status: "processing",
        Speed: "148x",
        ElapsedSeconds: 12.0,
        RemovedAudio: ["spa aac 2ch: removed"],
        RemovedSubtitles: ["ger subrip: removed"]);

    [Fact]
    public void A_file_that_was_never_updated_has_no_entry()
    {
        var store = new LiveProgressStore();

        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void An_update_appears_in_the_snapshot_by_its_path()
    {
        var store = new LiveProgressStore();

        store.Update("Show/S01E01.mkv", Writing(42.5));

        var snapshot = store.Snapshot();
        Assert.Equal(42.5, snapshot["Show/S01E01.mkv"].Percent);
        Assert.Equal("148x", snapshot["Show/S01E01.mkv"].Speed);
    }

    [Fact]
    public void A_later_update_to_the_same_path_replaces_the_earlier_one()
    {
        var store = new LiveProgressStore();

        store.Update("Film/Film.mkv", Writing(10));
        store.Update("Film/Film.mkv", Writing(55));

        Assert.Equal(55, store.Snapshot()["Film/Film.mkv"].Percent);
    }

    [Fact]
    public void Removing_a_path_drops_it_from_the_snapshot()
    {
        var store = new LiveProgressStore();
        store.Update("Film/Film.mkv", Writing(90));

        store.Remove("Film/Film.mkv");

        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void Removing_a_path_that_was_never_added_does_nothing()
    {
        var store = new LiveProgressStore();

        store.Remove("Never/There.mkv");

        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public async Task Several_files_updating_at_once_never_lose_or_corrupt_each_others_entries()
    {
        // The store is shared by every independent lane; one file's writer must never wait on, or clobber,
        // another's (#750).
        var store = new LiveProgressStore();
        const int Files = 8;
        const int UpdatesPerFile = 200;

        var writers = Enumerable.Range(0, Files).Select(file => Task.Run(() =>
        {
            for (var i = 0; i < UpdatesPerFile; i++)
            {
                store.Update($"Film{file}/Film.mkv", Writing(i % 100));
            }
        }));
        await Task.WhenAll(writers);

        var snapshot = store.Snapshot();
        Assert.Equal(Files, snapshot.Count);
        for (var file = 0; file < Files; file++)
        {
            Assert.True(snapshot.ContainsKey($"Film{file}/Film.mkv"));
        }
    }

    [Fact]
    public async Task Waiting_for_a_change_returns_as_soon_as_an_update_happens()
    {
        var store = new LiveProgressStore();
        var wait = store.WaitForChangeAsync(store.Version, TimeSpan.FromSeconds(5), TimeProvider.System);

        store.Update("Film/Film.mkv", Writing(1));

        var version = await wait.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(version);
        Assert.Equal(store.Version, version);
    }

    [Fact]
    public async Task Waiting_with_a_version_already_behind_returns_at_once()
    {
        var store = new LiveProgressStore();
        store.Update("Film/Film.mkv", Writing(1));
        var staleVersion = store.Version - 1;

        var version = await store.WaitForChangeAsync(staleVersion, TimeSpan.FromSeconds(5), TimeProvider.System);

        Assert.Equal(store.Version, version);
    }

    [Fact]
    public async Task Waiting_with_nothing_changing_times_out_to_null()
    {
        var store = new LiveProgressStore();

        // A zero timeout expires without an actual wall-clock wait.
        var version = await store.WaitForChangeAsync(store.Version, TimeSpan.Zero, TimeProvider.System);

        Assert.Null(version);
    }

    [Fact]
    public async Task One_update_wakes_every_waiter()
    {
        // Every open stream calls WaitForChangeAsync on the same store, so one change must reach them all (#750).
        var store = new LiveProgressStore();
        var waiters = Enumerable.Range(0, 5)
            .Select(_ => store.WaitForChangeAsync(store.Version, TimeSpan.FromSeconds(5), TimeProvider.System))
            .ToArray();

        store.Update("Film/Film.mkv", Writing(1));

        var results = await Task.WhenAll(waiters);
        Assert.All(results, version => Assert.Equal(store.Version, version));
    }
}
