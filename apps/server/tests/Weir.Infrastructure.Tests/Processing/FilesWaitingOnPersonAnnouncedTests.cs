using Microsoft.Extensions.Logging.Abstractions;
using Weir.Core.Processing;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.Processing;

/// <summary>
/// The tray counts the files waiting on a person from the queue's topic, so a file that leaves that list with no job moving (a
/// person removing it, a hand-off cancelled, a file that left its folder) says so itself, once the change has committed.
/// </summary>
public sealed class FilesWaitingOnPersonAnnouncedTests : IDisposable
{
    private readonly StoreFixture _store = new();
    private readonly DataChangePublisher _changes = new();
    private readonly FileStateStore _files;
    private readonly LibraryStore _libraries = new();

    public FilesWaitingOnPersonAnnouncedTests() => _files = new FileStateStore(_changes);

    public void Dispose() => _store.Dispose();

    private async Task<long> FileAsync(string status, string path = "Film/film.mkv")
    {
        await _store.Execute("INSERT INTO libraries (name, media_type) SELECT 'Films', 'movie' WHERE NOT EXISTS (SELECT 1 FROM libraries)");
        var library = await _store.Scalar("SELECT id FROM libraries");
        return await _store.Scalar(
            "INSERT INTO files (library_id, relative_path, status, status_reason, last_seen_at) " +
            $"VALUES ({library}, '{path}', '{status}', 'It went wrong.', '2000-01-01 00:00:00') RETURNING id");
    }

    private static async Task<List<string>> HeardAsync(BroadcastSubscription<string> heard)
    {
        heard.Dispose();
        var topics = new List<string>();
        await foreach (var topic in heard.ReadAllAsync(CancellationToken.None))
        {
            topics.Add(topic);
        }

        return topics;
    }

    [Fact]
    public async Task Forgetting_a_file_announces_the_change_once_it_commits_and_not_before()
    {
        var id = await FileAsync(ProcessingFileStatuses.ProcessingFailed);
        using var heard = _changes.Subscribe();

        await using (var uow = await UnitOfWork.OpenAsync(_store.Database))
        {
            await _files.ForgetAsync(uow, id);
            await uow.CommitAsync();
        }

        Assert.Equal([DataTopics.Jobs], await HeardAsync(heard));
        Assert.Equal(0, await _store.Scalar("SELECT count(*) FROM files"));
    }

    [Fact]
    public async Task Forgetting_a_file_that_is_not_there_or_whose_change_is_rolled_back_announces_nothing()
    {
        var id = await FileAsync(ProcessingFileStatuses.ProcessingFailed);
        using var heard = _changes.Subscribe();

        await using (var uow = await UnitOfWork.OpenAsync(_store.Database))
        {
            await _files.ForgetAsync(uow, id + 100);
            await _files.ForgetAsync(uow, id);
        }

        Assert.Empty(await HeardAsync(heard));
        Assert.Equal(1, await _store.Scalar("SELECT count(*) FROM files"));
    }

    [Fact]
    public async Task Cancelling_a_held_file_announces_the_change_and_cancelling_a_finished_one_does_not()
    {
        await FileAsync(ProcessingFileStatuses.OnHold, "Held/film.mkv");
        await FileAsync(ProcessingFileStatuses.Processed, "Done/film.mkv");
        var library = await _store.Scalar("SELECT id FROM libraries");
        using var held = _changes.Subscribe();
        using var done = _changes.Subscribe();

        await using (var uow = await UnitOfWork.OpenAsync(_store.Database))
        {
            await _files.MarkCancelledAsync(uow, library, "Done/film.mkv", "Cancelled.");
            await uow.CommitAsync();
        }

        Assert.Empty(await HeardAsync(done));

        await using (var uow = await UnitOfWork.OpenAsync(_store.Database))
        {
            await _files.MarkCancelledAsync(uow, library, "Held/film.mkv", "Cancelled.");
            await uow.CommitAsync();
        }

        Assert.Equal([DataTopics.Jobs], await HeardAsync(held));
    }

    [Fact]
    public async Task The_sweep_for_files_that_left_their_folder_announces_the_files_it_forgets()
    {
        var watched = _store.Home.Join("watch");
        Directory.CreateDirectory(watched);
        Directory.CreateDirectory(_store.Home.Join("out"));
        long libraryId;
        await using (var uow = await UnitOfWork.OpenAsync(_store.Database))
        {
            libraryId = (await _libraries.CreateAsync(uow, new ProcessingLibraryInput
            {
                Name = "Sweep tests",
                MediaType = ProcessingMediaScopes.Movie,
                WatchedFolder = watched,
                OutputFolder = _store.Home.Join("out"),
            })).Id;
            await uow.CommitAsync();
        }

        await _store.Execute(
            "INSERT INTO files (library_id, relative_path, status, status_reason, last_seen_at) " +
            $"VALUES ({libraryId}, 'Gone/film.mkv', 'processing_failed', 'It went wrong.', '2000-01-01 00:00:00')");
        using var heard = _changes.Subscribe();
        var sweep = new VanishedFileSweepTask(_store.Database, _store.Options, _libraries, _store.Clock, NullLogger<VanishedFileSweepTask>.Instance, _changes) { GoneLookAgain = _ => Task.CompletedTask };

        await sweep.RunOnceAsync(CancellationToken.None);

        Assert.Equal([DataTopics.Jobs, DataTopics.LibraryScan], await HeardAsync(heard));
        Assert.Equal(0, await _store.Scalar("SELECT count(*) FROM files"));
    }
}
