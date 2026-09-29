using Weir.Core.Processing;

namespace Weir.Infrastructure.Tests.Jobs;

/// <summary>
/// What a watched-folder scan holds a file to: a library's own minimum size and wait, or Settings › Performance's when it sets
/// none (#815).
/// </summary>
public sealed class ProcessingWatchedFolderScanIntakeTests
{
    private const long OneHourSeconds = 3600;

    [Fact]
    public async Task A_library_with_no_wait_of_its_own_holds_a_new_file_for_the_Performance_wait()
    {
        var (store, watched, scan) = await WatchedFolderScanFixture.StartAsync();
        using var _ = store;
        await store.Execute($"UPDATE operator_settings SET min_file_age_seconds = {OneHourSeconds}");
        var libraryId = await WatchedFolderScanFixture.LibraryAsync(store, watched, minFileAgeSeconds: null);

        await scan(libraryId);

        Assert.Equal(ProcessingFileStatuses.OnHold, (await WatchedFolderScanFixture.FileAsync(store, libraryId)).Status);
    }

    [Fact]
    public async Task A_library_with_its_own_wait_ignores_the_Performance_wait()
    {
        var (store, watched, scan) = await WatchedFolderScanFixture.StartAsync();
        using var _ = store;
        await store.Execute($"UPDATE operator_settings SET min_file_age_seconds = {OneHourSeconds}");
        var libraryId = await WatchedFolderScanFixture.LibraryAsync(store, watched, minFileAgeSeconds: 0);

        await scan(libraryId);

        Assert.Equal(ProcessingFileStatuses.Unprocessed, (await WatchedFolderScanFixture.FileAsync(store, libraryId)).Status);
    }

    [Fact]
    public async Task A_library_with_no_minimum_size_of_its_own_skips_files_under_the_Performance_minimum()
    {
        var (store, watched, scan) = await WatchedFolderScanFixture.StartAsync();
        using var _ = store;
        await store.Execute("UPDATE operator_settings SET min_input_file_size_mb = 1");
        var libraryId = await WatchedFolderScanFixture.LibraryAsync(store, watched, minFileAgeSeconds: 0, minFileSizeMb: null);

        await scan(libraryId);

        var file = await WatchedFolderScanFixture.FileAsync(store, libraryId);
        Assert.Equal(ProcessingFileStatuses.Skipped, file.Status);
        Assert.Contains("under the 1 MB minimum", file.StatusReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_library_with_its_own_minimum_size_ignores_the_Performance_minimum()
    {
        var (store, watched, scan) = await WatchedFolderScanFixture.StartAsync();
        using var _ = store;
        await store.Execute("UPDATE operator_settings SET min_input_file_size_mb = 1");
        var libraryId = await WatchedFolderScanFixture.LibraryAsync(store, watched, minFileAgeSeconds: 0, minFileSizeMb: 0);

        await scan(libraryId);

        Assert.Equal(ProcessingFileStatuses.Unprocessed, (await WatchedFolderScanFixture.FileAsync(store, libraryId)).Status);
    }
}
