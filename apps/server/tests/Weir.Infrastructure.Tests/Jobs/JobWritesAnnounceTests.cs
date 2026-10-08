using Weir.Core.LibraryMode;
using Weir.Core.MediaManagers;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.LibraryMode;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Tests.Activity;
using Weir.Infrastructure.Tests.MediaManagers;

namespace Weir.Infrastructure.Tests.Jobs;

/// <summary>The writes to job rows that do not go through the queue's own operations still tell the live stream.</summary>
public sealed class JobWritesAnnounceTests : IDisposable
{
    private readonly MediaManagerFixture _fixture = new();
    private readonly PublishedTopics _published;

    public JobWritesAnnounceTests() => _published = new PublishedTopics(_fixture.Changes);

    public void Dispose()
    {
        _published.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public async Task A_manager_cancelling_a_hand_off_announces_the_jobs_it_cancelled()
    {
        var watched = _fixture.Store.Home.Join("movies");
        await _fixture.LibraryAsync("movie", watched);
        var handoff = new MediaManagerImportEvent
        {
            SourceKey = "deluno",
            EventKind = "handoff",
            MediaScope = "movie",
            FilePath = Path.Join(watched, "Film", "film.mkv"),
            HandoffId = "h1",
            CallbackPath = "/api/integrations/processors/events",
            LibraryId = "lib-1",
        };
        await _fixture.Db(uow => _fixture.Intake.EnqueueRefineAsync(uow, handoff));
        var row = (await _fixture.Db(uow => HandoffLedgerStore.FindAsync(uow, "deluno", "h1")))!;
        await _published.TakeAsync();

        await _fixture.Db(uow => _fixture.Ledger.CancelAsync(uow, _fixture.Jobs, row));

        Assert.Equal([DataTopics.Jobs, DataTopics.FilesAtOnce], await _published.TakeAsync());
    }

    [Fact]
    public async Task Resetting_the_history_announces_the_finished_jobs_it_removed()
    {
        await _fixture.Store.Execute("INSERT INTO jobs (dedupe_key, job_kind, status) VALUES ('done', 'processing.test.v1', 'completed')");

        await _fixture.Db(uow => new OperationalHistoryStore(_fixture.Changes).ResetAsync(uow));

        Assert.Equal([DataTopics.Jobs], await _published.TakeAsync());
    }

    [Fact]
    public async Task Deleting_a_librarys_scan_and_clean_jobs_announces_them()
    {
        await _fixture.Store.Execute(
            $"INSERT INTO jobs (dedupe_key, job_kind, status) VALUES ('{LibraryModeJobKinds.CleanKind}:1:film.mkv', '{LibraryModeJobKinds.CleanKind}', 'pending')");
        await _fixture.Db(async uow =>
        {
            await new LibrarySettingsStore(_fixture.Changes).DeleteAllForLibraryAsync(uow, 1);
            return 0;
        });

        Assert.Equal([DataTopics.Jobs, DataTopics.FilesAtOnce], await _published.TakeAsync());
    }

    [Fact]
    public async Task Pruning_old_finished_jobs_announces_only_when_it_removed_some()
    {
        var retention = new JobRowsRetention(_fixture.Jobs);
        var later = DateTimeOffset.UtcNow.AddDays(1);

        await retention.RunTickAsync(jobRowsRetentionDays: 0, later);
        Assert.Empty(await _published.TakeAsync());

        await _fixture.Store.Execute("INSERT INTO jobs (dedupe_key, job_kind, status) VALUES ('old', 'processing.test.v1', 'completed')");
        await retention.RunTickAsync(jobRowsRetentionDays: 0, later);
        Assert.Equal([DataTopics.Jobs], await _published.TakeAsync());
    }
}
