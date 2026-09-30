using Weir.Core.Processing;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.Jobs;

/// <summary>
/// A workflow set to delete rejected files deletes only for a rule the workflow itself holds. A minimum size it follows from
/// Settings › Performance is not one: the file is skipped and left where it is.
/// </summary>
public sealed class ScanRejectedFileDeletionTests
{
    private static readonly FileStateStore Files = new();

    private static async Task<(string Watched, string Output, string Sample)> FoldersWithSampleAsync(StoreFixture store)
    {
        var watched = store.Home.Join("watch");
        var output = store.Home.Join("out");
        Directory.CreateDirectory(watched);
        Directory.CreateDirectory(output);
        var sample = Path.Combine(watched, "Film.2024.mkv");
        await File.WriteAllBytesAsync(sample, [1]);
        return (watched, output, sample);
    }

    private static async Task<ProcessingFileRecord?> RecordedAsync(StoreFixture store, long libraryId, string relativePath)
    {
        await using var uow = await UnitOfWork.OpenAsync(store.Database);
        return await Files.FindAsync(uow, libraryId, relativePath);
    }

    [Fact]
    public async Task A_file_under_the_minimum_size_the_workflow_follows_from_Performance_is_skipped_not_deleted()
    {
        var (store, jobs, handler) = await ProcessingWatchedFolderScanDispatchJobHandlerTests.BuildAsync();
        using var _ = store;
        await store.Execute("UPDATE operator_settings SET min_input_file_size_mb = 100");
        var (watched, output, sample) = await FoldersWithSampleAsync(store);
        var libraryId = await ProcessingWatchedFolderScanDispatchJobHandlerTests.CreateLibraryAsync(store, watched, output, rejectedFileAction: "delete_file");

        await ProcessingWatchedFolderScanDispatchJobHandlerTests.RunScanAsync(handler, jobs, libraryId, enqueueRemuxJobs: true);

        Assert.True(File.Exists(sample));
        var recorded = await RecordedAsync(store, libraryId, "Film.2024.mkv");
        Assert.Equal(ProcessingFileStatuses.Skipped, recorded!.Status);
        Assert.DoesNotContain("delete", recorded.StatusReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_file_under_the_minimum_size_the_workflow_sets_itself_is_deleted()
    {
        var (store, jobs, handler) = await ProcessingWatchedFolderScanDispatchJobHandlerTests.BuildAsync();
        using var _ = store;
        var (watched, output, sample) = await FoldersWithSampleAsync(store);
        var libraryId = await ProcessingWatchedFolderScanDispatchJobHandlerTests.CreateLibraryAsync(
            store, watched, output, rejectedFileAction: "delete_file", minFileSizeMb: 100);

        await ProcessingWatchedFolderScanDispatchJobHandlerTests.RunScanAsync(handler, jobs, libraryId, enqueueRemuxJobs: true);

        Assert.False(File.Exists(sample));
        var recorded = await RecordedAsync(store, libraryId, "Film.2024.mkv");
        Assert.Equal(ProcessingFileStatuses.Skipped, recorded!.Status);
    }
}
