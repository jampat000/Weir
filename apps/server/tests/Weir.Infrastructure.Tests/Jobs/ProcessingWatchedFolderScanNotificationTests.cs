using Weir.Infrastructure.Activity;

namespace Weir.Infrastructure.Tests.Jobs;

/// <summary>
/// What a watched-folder scan tells an open Processing page about the files it finds (#816): a scan records no Activity row
/// for them, so it moves the live stream's revision itself, once however many files it wrote.
/// </summary>
public sealed class ProcessingWatchedFolderScanNotificationTests
{
    [Fact]
    public async Task A_scan_that_finds_files_tells_open_pages_once_however_many_it_finds()
    {
        var (store, watched, scan) = await WatchedFolderScanFixture.StartAsync();
        using var _ = store;
        foreach (var name in new[] { "Second 2002.mkv", "Third 2003.mkv", "Fourth 2004.mkv" })
        {
            File.WriteAllBytes(Path.Combine(watched, name), [1]);
        }

        var libraryId = await WatchedFolderScanFixture.LibraryAsync(store, watched);
        var notifier = ActivityNotifications.For(store.Database);
        var before = notifier.Snapshot();

        await scan(libraryId);

        Assert.Equal(before.Version + 1, notifier.Snapshot().Version);
        Assert.Equal(before.LatestId, notifier.Snapshot().LatestId);
    }

    [Fact]
    public async Task A_scan_that_finds_nothing_new_tells_nobody()
    {
        var (store, watched, scan) = await WatchedFolderScanFixture.StartAsync();
        using var _ = store;
        var libraryId = await WatchedFolderScanFixture.LibraryAsync(store, watched);
        await scan(libraryId);
        var notifier = ActivityNotifications.For(store.Database);
        var afterFirstScan = notifier.Snapshot();

        await scan(libraryId);

        Assert.Equal(afterFirstScan, notifier.Snapshot());
    }
}
