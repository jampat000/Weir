using Weir.Core.Processing;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.Jobs;

/// <summary>A real database, a watched folder holding one small file and a way to scan it, for tests of what a scan decides and tells.</summary>
internal static class WatchedFolderScanFixture
{
    internal const string SmallFile = "Small 2001.mkv";

    private static readonly FileStateStore Files = new();

    /// <summary>The store, the watched folder, and a scan of one library that queues no passes. The operator's own limits are zero.</summary>
    internal static async Task<(StoreFixture Store, string Watched, Func<long, Task> Scan)> StartAsync()
    {
        var (store, jobs, handler) = await ProcessingWatchedFolderScanDispatchJobHandlerTests.BuildAsync();
        var watched = store.Home.Join("watch");
        Directory.CreateDirectory(watched);
        Directory.CreateDirectory(store.Home.Join("out"));
        File.WriteAllBytes(Path.Combine(watched, SmallFile), [1]);
        return (store, watched, libraryId => ProcessingWatchedFolderScanDispatchJobHandlerTests.RunScanAsync(handler, jobs, libraryId, enqueueRemuxJobs: false));
    }

    internal static Task<long> LibraryAsync(StoreFixture store, string watched, long? minFileAgeSeconds, long? minFileSizeMb = null) =>
        ProcessingWatchedFolderScanDispatchJobHandlerTests.CreateLibraryAsync(
            store, watched, store.Home.Join("out"), minFileAgeSeconds: minFileAgeSeconds, minFileSizeMb: minFileSizeMb);

    internal static async Task<ProcessingFileRecord> FileAsync(StoreFixture store, long libraryId, string relativePath = SmallFile)
    {
        await using var uow = await UnitOfWork.OpenAsync(store.Database);
        return await Files.FindAsync(uow, libraryId, relativePath) ?? throw new InvalidOperationException("The scan recorded no row for the file.");
    }
}
