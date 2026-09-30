using Weir.Core.Processing;

namespace Weir.Infrastructure.Tests.Jobs;

/// <summary>What a watched-folder scan holds a file to: its workflow's wait and minimum size, and nothing else.</summary>
public sealed class ProcessingWatchedFolderScanIntakeTests
{
    private const long OneHourSeconds = 3600;

    [Fact]
    public async Task A_new_file_is_held_for_the_workflows_wait()
    {
        var (store, watched, scan) = await WatchedFolderScanFixture.StartAsync();
        using var _ = store;
        var libraryId = await WatchedFolderScanFixture.LibraryAsync(store, watched, readyAfterSeconds: OneHourSeconds);

        await scan(libraryId);

        Assert.Equal(ProcessingFileStatuses.OnHold, (await WatchedFolderScanFixture.FileAsync(store, libraryId)).Status);
    }

    [Fact]
    public async Task A_workflow_that_waits_for_nothing_takes_a_new_file_at_once()
    {
        var (store, watched, scan) = await WatchedFolderScanFixture.StartAsync();
        using var _ = store;
        var libraryId = await WatchedFolderScanFixture.LibraryAsync(store, watched, readyAfterSeconds: 0);

        await scan(libraryId);

        Assert.Equal(ProcessingFileStatuses.Unprocessed, (await WatchedFolderScanFixture.FileAsync(store, libraryId)).Status);
    }

    [Fact]
    public async Task A_file_under_the_workflows_minimum_size_is_skipped()
    {
        var (store, watched, scan) = await WatchedFolderScanFixture.StartAsync();
        using var _ = store;
        var libraryId = await WatchedFolderScanFixture.LibraryAsync(store, watched, minFileSizeMb: 1);

        await scan(libraryId);

        var file = await WatchedFolderScanFixture.FileAsync(store, libraryId);
        Assert.Equal(ProcessingFileStatuses.Skipped, file.Status);
        Assert.Contains("under the 1 MB minimum", file.StatusReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_workflow_with_no_minimum_size_takes_a_file_of_any_size()
    {
        var (store, watched, scan) = await WatchedFolderScanFixture.StartAsync();
        using var _ = store;
        var libraryId = await WatchedFolderScanFixture.LibraryAsync(store, watched, minFileSizeMb: 0);

        await scan(libraryId);

        Assert.Equal(ProcessingFileStatuses.Unprocessed, (await WatchedFolderScanFixture.FileAsync(store, libraryId)).Status);
    }
}
