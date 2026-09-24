using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Core.Security;
using Weir.Infrastructure.Auth;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Settings;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.MediaManagers;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.Jobs;

/// <summary>
/// A watched-folder scan and the vanished-file sweep never hold the write lock while they walk folders and look at files, and a
/// row another lane changed after the scan read it keeps that lane's change (#708).
/// </summary>
[Collection(WriteLockTimingGroup.Name)]
public sealed class WatchedFolderScanLockTests : IDisposable
{
    /// <summary>
    /// The longest another lane may wait for the write lock. Far above any one of a bulk job's transactions, and well below
    /// what a scan or sweep holding the lock throughout costs at these sizes (several seconds).
    /// </summary>
    private static readonly TimeSpan PromptWrite = TimeSpan.FromSeconds(1);

    private const int ManyFiles = 5_000;

    private const int GoneRows = 10_000;

    private const int QueuedPasses = 50;

    private readonly StoreFixture _store = new(("WEIR_CREDENTIALS_SECRET", "scan-lock-tests-credentials-secret"));
    private readonly ProcessingJobStore _jobs;
    private readonly ProcessingWatchedFolderScanDispatchJobHandler _handler;
    private readonly LibraryStore _libraries = new();
    private readonly FileStateStore _files = new();
    private readonly string _watched;
    private readonly string _output;

    public WatchedFolderScanLockTests()
    {
        var cipher = new CredentialCipher(_store.Options.CredentialsSecret, _store.Options.SessionSecret, _store.Options.PreviousCredentialsSecrets, _store.Clock);
        var connections = new MediaManagerConnectionService(_store.Options, cipher, new HttpMediaManagerPorts(new FakeManagerHttp()), new MediaManagerConnectionStore());
        _jobs = new ProcessingJobStore(_store.Database, _store.Clock);
        _handler = new ProcessingWatchedFolderScanDispatchJobHandler(
            _store.Database, _store.Clock, _store.Options, _jobs, connections, new SuiteSettingsStore(new AuthStore()), new OperatorSettingsStore(), _libraries, _files);
        _watched = _store.Home.Join("watch");
        _output = _store.Home.Join("out");
        Directory.CreateDirectory(_watched);
        Directory.CreateDirectory(_output);
    }

    public void Dispose() => _store.Dispose();

    private async Task<long> LibraryAsync()
    {
        await _store.Execute(
            "INSERT INTO operator_settings (id, min_file_age_seconds, min_input_file_size_mb, minimum_free_disk_space_mb) VALUES (1, 0, 0, 0) " +
            "ON CONFLICT(id) DO UPDATE SET min_file_age_seconds = 0, min_input_file_size_mb = 0, minimum_free_disk_space_mb = 0");
        await using var uow = await UnitOfWork.OpenAsync(_store.Database);
        var created = await _libraries.CreateAsync(uow, new ProcessingLibraryInput
        {
            Name = "Scan lock tests",
            MediaType = ProcessingMediaScopes.Movie,
            WatchedFolder = _watched,
            OutputFolder = _output,
            // Every file is looked at in full at once: no settling interval, no minimum age.
            FileDetectionIntervalSeconds = 0,
            MinFileAgeSeconds = 0,
        });
        await uow.CommitAsync();
        return created.Id;
    }

    private Task ScanAsync(long libraryId, bool enqueueRemuxJobs)
    {
        var payload = new WireObject().Set("enqueue_remux_jobs", enqueueRemuxJobs).Set("scan_trigger", "manual").Set("media_scope", "movie").Set("library_id", libraryId);
        return _handler.HandleAsync(new JobWorkContext(1, ProcessingWatchedFolderScanDispatchJobKinds.ScanDispatch, WireJsonWriter.Dumps(payload, WireJsonFormat.Compact), "test"), CancellationToken.None);
    }

    /// <summary>
    /// Commits one-row writes, one after another, until <paramref name="work"/> (running on the thread pool, as a worker slot
    /// runs it) finishes. Each write times only how long it waited for the write lock, on a thread of its own, so a busy thread
    /// pool or a WAL checkpoint after a commit does not count. Returns the longest wait and how many writes ran while the work
    /// was still going.
    /// </summary>
    private async Task<(TimeSpan Longest, int WrittenAlongside)> WriteAlongsideAsync(Task work)
    {
        var writes = Task.Factory.StartNew(
            () =>
            {
                var longest = TimeSpan.Zero;
                var alongside = 0;
                while (!work.IsCompleted)
                {
                    using var connection = _store.Database.Open();
                    var started = Stopwatch.GetTimestamp();
                    using var transaction = connection.BeginTransaction(deferred: false);
                    var waited = Stopwatch.GetElapsedTime(started);
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = "INSERT INTO activity_events (event_type, module, title) VALUES ('test.alongside', 'test', 'a write from another lane')";
                        command.ExecuteNonQuery();
                    }

                    transaction.Commit();
                    longest = waited > longest ? waited : longest;
                    alongside += work.IsCompleted ? 0 : 1;
                }

                return (longest, alongside);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        var result = await writes;
        await work;
        return result;
    }

    [Fact]
    public async Task Another_lanes_writes_go_through_while_a_large_scan_runs()
    {
        var libraryId = await LibraryAsync();
        for (var index = 0; index < ManyFiles; index++)
        {
            File.WriteAllBytes(Path.Join(_watched, $"Film {index:D5}.mkv"), [1]);
        }

        var (longest, alongside) = await WriteAlongsideAsync(Task.Run(() => ScanAsync(libraryId, enqueueRemuxJobs: false)));

        Assert.True(longest < PromptWrite, $"a write waited {longest.TotalMilliseconds:F0} ms for the lock");
        Assert.True(alongside > 0, "the writes ran while the scan was still going");
        Assert.Equal(ManyFiles, await _store.Scalar($"SELECT count(*) FROM files WHERE library_id = {libraryId}"));
    }

    [Fact]
    public async Task Another_lanes_writes_go_through_while_the_vanished_file_sweep_forgets_many_files()
    {
        var libraryId = await LibraryAsync();
        await _store.Execute(
            $"WITH RECURSIVE n(i) AS (SELECT 0 UNION ALL SELECT i + 1 FROM n WHERE i < {GoneRows - 1}) " +
            $"INSERT INTO files (library_id, relative_path, status, status_reason, last_seen_at) SELECT {libraryId}, 'Gone/' || i || '.mkv', 'unprocessed', '', '2000-01-01 00:00:00' FROM n");
        for (var index = 0; index < QueuedPasses; index++)
        {
            var payload = new WireObject().Set("relative_media_path", $"Queued/{index}.mkv").Set("media_scope", "movie").Set("library_id", libraryId);
            await _jobs.EnqueueOrGetAsync($"queued-{index}", "processing.file.remux_pass.v1", WireJsonWriter.Dumps(payload, WireJsonFormat.Compact));
        }

        var sweep = new VanishedFileSweepTask(_store.Database, _store.Options, _libraries, _store.Clock, NullLogger<VanishedFileSweepTask>.Instance);

        var (longest, alongside) = await WriteAlongsideAsync(Task.Run(() => sweep.RunOnceAsync(CancellationToken.None)));

        Assert.True(longest < PromptWrite, $"a write waited {longest.TotalMilliseconds:F0} ms for the lock");
        Assert.True(alongside > 0, "the writes ran while the sweep was still going");
        Assert.Equal(0, await _store.Scalar($"SELECT count(*) FROM files WHERE library_id = {libraryId}"));
    }

    [Fact]
    public async Task A_status_another_lane_set_after_the_scan_read_the_row_is_kept()
    {
        var libraryId = await LibraryAsync();
        var rowId = await _store.WithUnitOfWork(uow => _files.RecordFileStateAsync(
            uow, libraryId, "Film.mkv", new FileStateVerdict(ProcessingFileStatuses.Unprocessed, "Waiting."), 10, null, _store.Clock.GetUtcNow()));
        await _store.Execute($"UPDATE files SET status = 'processing', status_reason = 'A pass started.' WHERE id = {rowId}");
        var batch = new WatchedFolderScanBatch(_store.Database, _files, libraryId, _store.Clock.GetUtcNow());
        var queued = false;

        batch.Record(
            new ScannedFileWrite("Film.mkv", rowId, ProcessingFileStatuses.Unprocessed, null, new FileStateVerdict(ProcessingFileStatuses.OnHold, "Still being written."), 10, null),
            () =>
            {
                queued = true;
                return Task.CompletedTask;
            });
        await batch.FlushAsync(CancellationToken.None);

        Assert.False(queued, "nothing that depends on the write follows it");
        Assert.Equal(1, await _store.Scalar($"SELECT count(*) FROM files WHERE id = {rowId} AND status = 'processing' AND status_reason = 'A pass started.'"));
    }

    [Fact]
    public async Task A_file_a_hand_off_recorded_after_the_scan_read_the_library_keeps_the_hand_offs_state()
    {
        var libraryId = await LibraryAsync();
        var batch = new WatchedFolderScanBatch(_store.Database, _files, libraryId, _store.Clock.GetUtcNow());
        await _store.WithUnitOfWork(uow => _files.RecordFileStateAsync(
            uow, libraryId, "Film.mkv", new FileStateVerdict(ProcessingFileStatuses.Unprocessed, "Handed over by Radarr."), 10, null, _store.Clock.GetUtcNow()));

        batch.Record(new ScannedFileWrite("Film.mkv", null, null, null, new FileStateVerdict(ProcessingFileStatuses.OnHold, "Still being written."), 10, null));
        await batch.FlushAsync(CancellationToken.None);

        Assert.Equal(1, await _store.Scalar($"SELECT count(*) FROM files WHERE library_id = {libraryId} AND status_reason = 'Handed over by Radarr.'"));
    }

    [Fact]
    public async Task A_state_the_scan_read_is_replaced_by_its_new_verdict()
    {
        var libraryId = await LibraryAsync();
        var rowId = await _store.WithUnitOfWork(uow => _files.RecordFileStateAsync(
            uow, libraryId, "Film.mkv", new FileStateVerdict(ProcessingFileStatuses.ProcessingFailed, "It failed."), 10, null, _store.Clock.GetUtcNow()));
        await _store.Execute($"UPDATE files SET failure_attempts = 3 WHERE id = {rowId}");
        var batch = new WatchedFolderScanBatch(_store.Database, _files, libraryId, _store.Clock.GetUtcNow());

        batch.Record(new ScannedFileWrite(
            "Film.mkv", rowId, ProcessingFileStatuses.ProcessingFailed, ProcessingFileStatuses.Unprocessed, new FileStateVerdict(ProcessingFileStatuses.OnHold, "Changed; settling."), 20, null));
        await batch.FlushAsync(CancellationToken.None);

        Assert.Equal(1, await _store.Scalar($"SELECT count(*) FROM files WHERE id = {rowId} AND status = 'on_hold' AND failure_attempts = 0 AND size_bytes = 20"));
    }

    [Fact]
    public async Task A_pass_already_queued_for_a_file_is_found_through_its_index()
    {
        var plan = await _store.WithUnitOfWork(
            uow => uow.QueryAsync("EXPLAIN QUERY PLAN " + ActiveRemuxPasses.ForPathSql, reader => reader.GetString(3), ("@path", "Film.mkv")),
            commit: false);

        Assert.Contains(plan, step => step.Contains("ix_jobs_active_remux_pass_path", StringComparison.Ordinal));
    }
}
