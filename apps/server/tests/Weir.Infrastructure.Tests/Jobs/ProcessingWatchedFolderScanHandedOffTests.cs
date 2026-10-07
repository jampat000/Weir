using Microsoft.Extensions.Logging.Abstractions;
using Weir.Core.Processing;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.Jobs;

/// <summary>
/// A workflow linked to Deluno is processed only from Deluno's hand-off, so no way of asking for a scan (the timer, a change in
/// the watched folder, "scan now") queues one for it. A Sonarr-, Radarr- or other-manager-linked workflow and a Weir-only one
/// are scanned as before.
/// </summary>
public sealed class ProcessingWatchedFolderScanHandedOffTests
{
    private static readonly LibraryStore Libraries = new();
    private static readonly OperatorSettingsStore OperatorSettings = new();

    private static async Task<(long LibraryId, string Watched, string Output)> LinkedLibraryAsync(JobsTestDatabase db, TempDirectory dir, string? kind)
    {
        var watched = dir.Join("watched");
        var output = dir.Join("output");
        Directory.CreateDirectory(watched);
        Directory.CreateDirectory(output);
        db.Execute(
            "INSERT INTO libraries (name, media_type, watched_folder, output_folder) VALUES ('Movies', 'movie', @watched, @output)",
            ("@watched", watched), ("@output", output));
        var libraryId = db.Count("SELECT id FROM libraries WHERE name = 'Movies'");
        if (kind is not null)
        {
            db.Execute("INSERT INTO media_manager_connections (kind, name, base_url) VALUES (@kind, @kind, 'http://manager.local')", ("@kind", kind));
            db.Execute(
                "INSERT INTO library_manager_links (library_id, connection_id) SELECT @lib, id FROM media_manager_connections WHERE kind = @kind",
                ("@lib", libraryId), ("@kind", kind));
        }

        return (libraryId, watched, output);
    }

    private static async Task<ProcessingLibraryRecord> GetAsync(JobsTestDatabase db, long id)
    {
        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        return await Libraries.GetAsync(uow, id) ?? throw new InvalidOperationException("Library not found.");
    }

    [Theory]
    [InlineData("sonarr")]
    [InlineData("radarr")]
    [InlineData("native")]
    [InlineData(null)]
    public async Task Every_way_of_asking_for_a_scan_still_works_for_a_workflow_deluno_does_not_feed(string? kind)
    {
        using var dir = new TempDirectory();
        using var db = new JobsTestDatabase();
        var (libraryId, _, _) = await LinkedLibraryAsync(db, dir, kind);
        var library = await GetAsync(db, libraryId);

        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        var (valid, error) = await ProcessingWatchedFolderScanDispatchEnqueue.ValidatePrerequisitesAsync(uow, Libraries, enqueueRemuxJobs: true, "movie", libraryId);
        var (periodic, _) = await ProcessingWatchedFolderScanDispatchEnqueue.TryEnqueuePeriodicAsync(uow, db.Store, Libraries, library, enqueueRemuxJobs: true);
        await uow.CommitAsync();

        Assert.True(valid);
        Assert.Null(error);
        Assert.True(periodic);
    }

    [Fact]
    public async Task No_way_of_asking_for_a_scan_queues_one_for_a_workflow_deluno_feeds()
    {
        using var dir = new TempDirectory();
        using var db = new JobsTestDatabase();
        var (libraryId, _, _) = await LinkedLibraryAsync(db, dir, "deluno");
        var library = await GetAsync(db, libraryId);

        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        var (valid, error) = await ProcessingWatchedFolderScanDispatchEnqueue.ValidatePrerequisitesAsync(uow, Libraries, enqueueRemuxJobs: true, "movie", libraryId);
        var (periodic, periodicSkip) = await ProcessingWatchedFolderScanDispatchEnqueue.TryEnqueuePeriodicAsync(uow, db.Store, Libraries, library, enqueueRemuxJobs: true);
        var (watcher, watcherSkip) = await ProcessingWatchedFolderScanDispatchEnqueue.TryEnqueueForWatcherEventAsync(uow, db.Store, Libraries, library, enqueueRemuxJobs: true);
        await uow.CommitAsync();

        Assert.False(valid);
        Assert.Equal(ScanDispatchPrerequisiteError.HandedOffByManager, error);
        Assert.False(periodic);
        Assert.Equal("handed_off_by_manager", periodicSkip);
        Assert.False(watcher);
        Assert.Equal("handed_off_by_manager", watcherSkip);
        Assert.Equal(0, db.Count("SELECT COUNT(*) FROM jobs WHERE job_kind = @kind", ("@kind", ProcessingWatchedFolderScanDispatchJobKinds.ScanDispatch)));
    }

    [Theory]
    [InlineData("deluno", false)]
    [InlineData("sonarr", true)]
    [InlineData(null, true)]
    public async Task The_scheduler_never_ticks_for_a_workflow_deluno_feeds(string? kind, bool scheduled)
    {
        using var store = new StoreFixture(("WEIR_CREDENTIALS_SECRET", "handed-off-schedule-tests-secret"));
        var watched = store.Home.Join("watch");
        var output = store.Home.Join("out");
        Directory.CreateDirectory(watched);
        Directory.CreateDirectory(output);
        long libraryId;
        await using (var uow = await UnitOfWork.OpenAsync(store.Database))
        {
            var seeded = await Libraries.SeededForScopeAsync(uow, ProcessingMediaScopes.Movie) ?? throw new InvalidOperationException("No seeded Movies library.");
            libraryId = (await Libraries.UpdateAsync(uow, seeded, new ProcessingLibraryInput
            {
                Name = seeded.Name, MediaType = ProcessingMediaScopes.Movie, WatchedFolder = watched, OutputFolder = output, ScanIntervalSeconds = 10,
            })).Id;
            if (kind is not null)
            {
                await uow.ExecuteAsync("INSERT INTO media_manager_connections (kind, name, base_url) VALUES ($kind, $kind, 'http://manager.local')", ("$kind", kind));
                await uow.ExecuteAsync(
                    "INSERT INTO library_manager_links (library_id, connection_id) SELECT $lib, id FROM media_manager_connections", ("$lib", libraryId));
            }

            await uow.CommitAsync();
        }

        var task = new ProcessingWatchedFolderScanDispatchScheduleTask(
            store.Database, store.Options, new ProcessingJobStore(store.Database, store.Clock), OperatorSettings, Libraries, store.Clock,
            NullLogger<ProcessingWatchedFolderScanDispatchScheduleTask>.Instance);

        for (var i = 0; i < 3; i++)
        {
            await task.RunOnceAsync(CancellationToken.None);
            store.Clock.Set(store.Clock.GetUtcNow() + TimeSpan.FromSeconds(15));
        }

        var queued = await store.Scalar("SELECT COUNT(*) FROM jobs WHERE job_kind = 'processing.watched_folder.remux_scan_dispatch.v1' AND status = 'pending'");
        Assert.Equal(scheduled, queued > 0);
    }
}
