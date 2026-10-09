using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Core.Processing.RemuxPass;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Auth;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Media;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Processing.RemuxPass;
using Weir.Infrastructure.Settings;
using Weir.Infrastructure.Tests.Media;
using Weir.Infrastructure.Tests.MediaManagers;

namespace Weir.Infrastructure.Tests.Processing.RemuxPass;

/// <summary>
/// A file Weir cannot read is never handed back, and a file, or the folder holding it, that is deleted while Weir has work queued
/// for it is nothing to do: one plain Information row, and no Error or Warning row, failed job or retry. Real database, folders,
/// scan, handlers and worker loop; only ffprobe and ffmpeg are fakes.
/// </summary>
public sealed class VanishedSourceTests : IDisposable
{
    private const string Rel = "Film.2024/Film.2024.mkv";

    private const string UnreadableProbe =
        "[mov,mp4,m4a,3gp,3g2,mj2 @ 000001b280590a00] moov atom not found\nC:\\Weir\\Ready\\Movies\\Film.2024.mkv: Invalid data found when processing input";

    private readonly MediaManagerFixture _fixture = new();
    private readonly PassFolders _folders = new();
    private readonly FakeMediaRunner _media = new();
    private long _libraryId;

    public VanishedSourceTests()
    {
        _fixture.Store.Clock.Set(DateTimeOffset.UtcNow);
        _fixture.Store.Execute("UPDATE operator_settings SET minimum_free_disk_space_mb = 0").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _folders.Dispose();
        _fixture.Dispose();
    }

    private async Task SetUpAsync(string failurePolicy = "pass_through")
    {
        await _fixture.Store.Execute("DELETE FROM libraries");
        _libraryId = Convert.ToInt64(await _fixture.Db(uow => uow.ExecuteScalarWriteAsync(
            "INSERT INTO libraries (name, media_type, watched_folder, output_folder, work_folder, failure_policy, max_attempts, " +
            "rejected_file_action, retry_backoff_seconds, ready_after_seconds, min_file_size_mb, display_order, sidecar_patterns_csv, remove_original_after_success) " +
            "VALUES ('Library', 'movie', $w, $o, $k, $policy, 3, 'leave', 60, 0, 0, 1, '.srt', 1) RETURNING id",
            ("$w", _folders.Watched),
            ("$o", _folders.Output),
            ("$k", _folders.Work),
            ("$policy", failurePolicy))), CultureInfo.InvariantCulture);
        _media.DefaultProbe = FakeMediaRunner.EnglishOnly;
        _media.Probes["Film.2024.mkv"] = FakeMediaRunner.EnglishAndJapanese;
    }

    private RemuxPassHandler RemuxHandler()
    {
        var data = new SqliteRemuxPassData(_fixture.Store.Database, _fixture.Connections, NullLogger<SqliteRemuxPassData>.Instance);
        var runner = new RemuxPassRunner(
            new MediaTools(_media, new FixedResolver(), new ListLogger<MediaTools>(), _fixture.Store.Clock),
            new FixedResolver(),
            data,
            data,
            new TvSeasonFolderCleanup(_fixture.Store.Database, _fixture.Connections, _fixture.Store.Clock, NullLogger<TvSeasonFolderCleanup>.Instance),
            new FakeOriginalLanguage(),
            new RemuxPassSettings(),
            _fixture.Store.Clock,
            NullLogger<RemuxPassRunner>.Instance);
        return new RemuxPassHandler(
            _fixture.Store.Database,
            _fixture.Store.Options,
            runner,
            new QueueingFailurePolicy(_fixture.Jobs),
            _fixture.OperatorSettings,
            _fixture.Handback,
            _fixture.Libraries,
            _fixture.Store.Clock,
            NullLogger<RemuxPassHandler>.Instance,
            new DownloadedScanNotifier(_fixture.Connections, _fixture.ConnectionStore, _fixture.Libraries, _fixture.Http, NullLogger<DownloadedScanNotifier>.Instance),
            _fixture.Reporter,
            _fixture.Jobs);
    }

    private ProcessingPassThroughHandler PassThroughHandler() =>
        new(_fixture.Store.Database, _fixture.Store.Clock, NullLogger<ProcessingPassThroughHandler>.Instance, _fixture.Handback, _fixture.Libraries, _fixture.Jobs, _fixture.Reporter);

    private ProcessingRejectHandler RejectHandler() =>
        new(
            _fixture.Store.Database,
            _fixture.Ports,
            _fixture.Connections,
            _fixture.Reporter,
            new RejectRoutes(_fixture.Ports, _fixture.Reporter),
            _fixture.Ledger,
            _fixture.Jobs,
            new RejectPacing(TimeProvider.System),
            _fixture.Libraries,
            _fixture.Store.Clock,
            NullLogger<ProcessingRejectHandler>.Instance);

    private ProcessingJobProcessor Worker() => new(
        _fixture.Jobs,
        new JobHandlerRegistry([RemuxHandler(), PassThroughHandler(), RejectHandler()]),
        new SqliteActivityWriter(_fixture.Store.Database),
        new NoUnhandledJobFailureRecorder(),
        new NoJobNotifications(),
        _fixture.Store.Clock,
        NullLogger<ProcessingJobProcessor>.Instance);

    private ProcessingWatchedFolderScanDispatchJobHandler ScanHandler(ILogger<ProcessingWatchedFolderScanDispatchJobHandler>? logger = null) => new(
        _fixture.Store.Database, _fixture.Store.Clock, _fixture.Store.Options, _fixture.Jobs, _fixture.Connections,
        new SuiteSettingsStore(new AuthStore()), _fixture.Libraries, _fixture.Files, new FileSkipMarkerStore(), logger: logger);

    private async Task ScanAsync(ILogger<ProcessingWatchedFolderScanDispatchJobHandler>? logger = null)
    {
        var payload = new WireObject().Set("enqueue_remux_jobs", true).Set("scan_trigger", "watcher").Set("media_scope", "movie").Set("library_id", _libraryId);
        var job = await _fixture.Jobs.EnqueueOrGetAsync(
            $"scan-{Guid.NewGuid():N}", ProcessingWatchedFolderScanDispatchJobKinds.ScanDispatch, WireJsonWriter.Dumps(payload, WireJsonFormat.Compact));
        try
        {
            await ScanHandler(logger).HandleAsync(new JobWorkContext(job.Id, job.JobKind, job.PayloadJson, "scan-owner"), CancellationToken.None);
        }
        finally
        {
            await _fixture.Store.Execute($"UPDATE jobs SET status = 'completed' WHERE id = {job.Id}");
        }
    }

    private async Task DrainAsync()
    {
        var worker = Worker();
        for (var i = 0; i < 20; i++)
        {
            if (await worker.ProcessOneAsync("test-worker") == JobProcessOutcome.Idle)
            {
                return;
            }
        }

        Assert.Fail("The worker never went idle.");
    }

    private async Task ScanAndDrainAsync()
    {
        await ScanAsync();
        await DrainAsync();
    }

    private void Advance(int minutes) => _fixture.Store.Clock.Set(_fixture.Store.Clock.GetUtcNow().AddMinutes(minutes));

    private Task<long> JobsOfKindAsync(string kind) => _fixture.Store.Scalar($"SELECT count(*) FROM jobs WHERE job_kind = '{kind}'");

    private Task<long> PassThroughJobsAsync() => JobsOfKindAsync(IntakeRules.PassThroughJobKind);

    /// <summary>Jobs that failed or are waiting to try again: neither is what a deleted file should cause.</summary>
    private Task<long> FailedOrRetriedJobsAsync() =>
        _fixture.Store.Scalar("SELECT count(*) FROM jobs WHERE status = 'failed' OR last_error IS NOT NULL OR attempt_count > 1");

    /// <summary>Activity a person would see as an error or a warning.</summary>
    private Task<long> ErrorsAndWarningsAsync() =>
        _fixture.Store.Scalar("SELECT count(*) FROM activity_events WHERE result IN ('failed', 'warning', 'retrying')");

    private Task<long> FilesAsync() => _fixture.Store.Scalar("SELECT count(*) FROM files");

    private async Task<string?> StatusAsync(string relative)
    {
        using var connection = _fixture.Store.Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM files WHERE relative_path = $p";
        command.Parameters.AddWithValue("$p", relative);
        return await command.ExecuteScalarAsync() is { } value and not DBNull ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>The rows that say the file is gone: the one Information row the operator is owed.</summary>
    private Task<long> GoneRowsAsync() =>
        _fixture.Store.Scalar("SELECT count(*) FROM activity_events WHERE result = 'skipped' AND title LIKE '%is no longer there, so there is nothing to do'");

    /// <summary>Runs the scan and the waits until a file that cannot be read has used up all its looks.</summary>
    private async Task WaitOutTheLooksAsync(int looks)
    {
        for (var i = 0; i < looks; i++)
        {
            Advance(RemuxPassHandler.UnreadableWaitMinutes[i] + 1);
            await DrainAsync();
        }
    }

    // --- an unreadable file is never passed through -------------------------------------------------------------------

    [Fact]
    public async Task An_unreadable_file_that_is_still_there_when_its_wait_ends_is_left_where_it_is_under_a_reject_workflow()
    {
        await SetUpAsync(failurePolicy: "reject");
        _media.ProbeError = UnreadableProbe;
        var source = _folders.Source(Path.Join("Film.2024", "Film.2024.mkv"));

        await ScanAndDrainAsync();
        Assert.Equal("on_hold", await StatusAsync(Rel));
        await WaitOutTheLooksAsync(RemuxPassHandler.UnreadableWaitMinutes.Count);
        await DrainAsync();

        // With no media manager to take the rejection, a reject workflow used to fall back to handing the file back.
        Assert.Equal(0, await PassThroughJobsAsync());
        Assert.False(File.Exists(_folders.Out(Rel)), "a file Weir could not read is never handed back");
        Assert.True(File.Exists(source));
        Assert.Equal("rejected", await StatusAsync(Rel));
        Assert.Equal(0, await _fixture.Store.Scalar("SELECT count(*) FROM activity_events WHERE event_type = 'processing.file_reject_fell_back'"));
    }

    [Fact]
    public async Task An_unreadable_file_that_is_still_there_when_its_wait_ends_is_left_where_it_is_under_a_pass_through_workflow()
    {
        await SetUpAsync();
        _media.ProbeError = UnreadableProbe;
        var source = _folders.Source(Path.Join("Film.2024", "Film.2024.mkv"));

        await ScanAndDrainAsync();
        await WaitOutTheLooksAsync(RemuxPassHandler.UnreadableWaitMinutes.Count);

        Assert.Equal(0, await PassThroughJobsAsync());
        Assert.False(File.Exists(_folders.Out(Rel)));
        Assert.True(File.Exists(source));
        Assert.Equal("rejected", await StatusAsync(Rel));
    }

    // --- a file deleted while Weir waits is nothing to do -------------------------------------------------------------

    [Fact]
    public async Task An_unreadable_file_deleted_while_it_waits_settles_with_no_failed_job_and_one_information_row()
    {
        await SetUpAsync();
        _media.ProbeError = UnreadableProbe;
        _folders.Source(Path.Join("Film.2024", "Film.2024.mkv"));

        await ScanAndDrainAsync();
        Assert.Equal("on_hold", await StatusAsync(Rel));

        Directory.Delete(Path.Join(_folders.Watched, "Film.2024"), recursive: true);
        Advance(RemuxPassHandler.UnreadableWaitMinutes[0] + 1);
        await DrainAsync();

        Assert.Equal(0, await PassThroughJobsAsync());
        Assert.Equal(0, await FailedOrRetriedJobsAsync());
        Assert.Equal(0, await ErrorsAndWarningsAsync());
        Assert.Equal(1, await GoneRowsAsync());
        Assert.Null(await StatusAsync(Rel));
        Assert.False(File.Exists(_folders.Out(Rel)));
    }

    [Fact]
    public async Task A_file_deleted_after_the_scan_queued_it_settles_with_no_failed_job_and_nothing_handed_back()
    {
        await SetUpAsync();
        _folders.Source(Path.Join("Film.2024", "Film.2024.mkv"));

        await ScanAsync();
        Assert.Equal(1, await JobsOfKindAsync(RemuxPassOutcomes.JobKind));
        Directory.Delete(Path.Join(_folders.Watched, "Film.2024"), recursive: true);
        await DrainAsync();

        Assert.Equal(0, await PassThroughJobsAsync());
        Assert.Equal(0, await FailedOrRetriedJobsAsync());
        Assert.Equal(0, await ErrorsAndWarningsAsync());
        Assert.Equal(1, await GoneRowsAsync());
        Assert.Equal(0, await FilesAsync());
        Assert.Empty(_media.Remuxes);
    }

    [Fact]
    public async Task A_handed_back_file_deleted_before_its_copy_settles_with_no_failed_job()
    {
        await SetUpAsync();
        var source = _folders.Source(Path.Join("Film.2024", "Film.2024.mkv"));
        await _fixture.Store.Execute(
            $"INSERT INTO files (library_id, relative_path, status, status_reason) VALUES ({_libraryId}, '{Rel}', 'processing_failed', 'failed')");
        var payload = new WireObject().Set("relative_media_path", Rel).Set("library_id", _libraryId).Set("trigger", "worker");
        await _fixture.Jobs.EnqueueOrGetAsync("pass-through-1", IntakeRules.PassThroughJobKind, WireJsonWriter.Dumps(payload, WireJsonFormat.Compact));
        File.Delete(source);

        await DrainAsync();

        Assert.Equal(0, await FailedOrRetriedJobsAsync());
        Assert.Equal(0, await ErrorsAndWarningsAsync());
        Assert.Equal(1, await GoneRowsAsync());
        Assert.Equal(0, await FilesAsync());
    }

    // --- scanning ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_scan_after_a_watched_subfolder_was_deleted_has_nothing_to_queue_and_does_not_fail()
    {
        await SetUpAsync();
        _folders.Source(Path.Join("Film.2024", "Film.2024.mkv"));
        _folders.Source(Path.Join("Other.2023", "Other.2023.mkv"));
        _media.ProbeError = UnreadableProbe;
        await ScanAndDrainAsync();
        Directory.Delete(Path.Join(_folders.Watched, "Film.2024"), recursive: true);

        await ScanAndDrainAsync();

        Assert.Equal(0, await FailedOrRetriedJobsAsync());
        Assert.Equal(0, await ErrorsAndWarningsAsync());
        Assert.Equal(0, await PassThroughJobsAsync());
        Assert.True(File.Exists(Path.Join(_folders.Watched, "Other.2023", "Other.2023.mkv")));
    }

    [Fact]
    public async Task A_scan_of_a_watched_folder_that_is_gone_warns_in_plain_words_and_does_not_fail_the_job()
    {
        await SetUpAsync();
        Directory.Delete(_folders.Watched, recursive: true);
        var log = new ListLogger<ProcessingWatchedFolderScanDispatchJobHandler>();

        await ScanAsync(log);

        var warning = Assert.Single(log.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("can't see its watched folder", warning.Message, StringComparison.Ordinal);
        Assert.Contains(_folders.Watched, warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(log.Entries, entry => entry.Level >= LogLevel.Error);
        Assert.Equal(0, await JobsOfKindAsync(RemuxPassOutcomes.JobKind));
    }
}
