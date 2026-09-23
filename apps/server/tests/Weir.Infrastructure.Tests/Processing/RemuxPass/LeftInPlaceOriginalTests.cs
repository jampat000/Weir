using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Core.Processing.RemuxPass;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Media;
using Weir.Infrastructure.Processing.RemuxPass;
using Weir.Infrastructure.Tests.Media;
using Weir.Infrastructure.Tests.MediaManagers;

namespace Weir.Infrastructure.Tests.Processing.RemuxPass;

/// <summary>
/// What the watched-folder scan does with a file it has already dealt with, and with one that has gone. Real database,
/// folders, scan, handler and worker loop; only ffprobe and ffmpeg are fakes.
/// <list type="bullet">
/// <item>#644: a library that removes originals but had to leave one in place (TV season cleanup skipped, a Movies removal
/// interrupted) must not clean that file again on every scan once the manager has imported the output.</item>
/// <item>#645: a file that leaves the watched folder before Weir finishes with it stops being listed.</item>
/// <item>#643: a file whose queued pass was cancelled is left alone until it changes.</item>
/// </list>
/// </summary>
public sealed class LeftInPlaceOriginalTests : IDisposable
{
    private readonly MediaManagerFixture _fixture = new();
    private readonly PassFolders _folders = new();
    private readonly FakeMediaRunner _media = new();
    private long _libraryId;

    public LeftInPlaceOriginalTests()
    {
        _fixture.Store.Clock.Set(DateTimeOffset.UtcNow);
        _fixture.Store.Execute(
            "UPDATE operator_settings SET min_file_age_seconds = 0, min_input_file_size_mb = 0, minimum_free_disk_space_mb = 0")
            .GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _folders.Dispose();
        _fixture.Dispose();
    }

    private async Task SetUpAsync(string mediaType, bool removeOriginal = true, int minFileAgeSeconds = 0)
    {
        await _fixture.Store.Execute("DELETE FROM libraries");
        _libraryId = Convert.ToInt64(await _fixture.Db(uow => uow.ExecuteScalarWriteAsync(
            "INSERT INTO libraries (name, media_type, watched_folder, output_folder, work_folder, failure_policy, max_attempts, " +
            "rejected_file_action, retry_backoff_seconds, min_file_age_seconds, file_detection_interval_seconds, display_order, " +
            "sidecar_patterns_csv, remove_original_after_success) " +
            "VALUES ('Library', $t, $w, $o, $k, 'pass_through', 3, 'leave', 60, $age, 0, 1, '.srt', $remove) RETURNING id",
            ("$t", mediaType),
            ("$w", _folders.Watched),
            ("$o", _folders.Output),
            ("$k", _folders.Work),
            ("$age", minFileAgeSeconds),
            ("$remove", removeOriginal ? 1 : 0))), CultureInfo.InvariantCulture);
        // Sources carry a Japanese track the rules drop; the written output is probed with the default and reads English only.
        _media.DefaultProbe = FakeMediaRunner.EnglishOnly;
        _media.Probes["Film.2024.mkv"] = FakeMediaRunner.EnglishAndJapanese;
        _media.Probes["Show.S01E01.mkv"] = FakeMediaRunner.EnglishAndJapanese;
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
            new RemuxPassSettings { WatchedFolderMinFileAgeSeconds = 0 },
            _fixture.Store.Clock,
            NullLogger<RemuxPassRunner>.Instance);
        return new RemuxPassHandler(
            _fixture.Store.Database,
            _fixture.Store.Options,
            runner,
            new QueueingFailurePolicy(_fixture.Jobs),
            _fixture.Store.Clock,
            NullLogger<RemuxPassHandler>.Instance,
            _fixture.Reporter,
            _fixture.Jobs);
    }

    private ProcessingJobProcessor Worker() => new(
        _fixture.Jobs,
        new JobHandlerRegistry([RemuxHandler()]),
        new SqliteActivityWriter(_fixture.Store.Database),
        new NoUnhandledJobFailureRecorder(),
        new NoJobNotifications(),
        _fixture.Store.Clock,
        NullLogger<ProcessingJobProcessor>.Instance);

    private async Task ScanAsync(string mediaType)
    {
        var scan = new ProcessingWatchedFolderScanDispatchJobHandler(
            _fixture.Store.Database, _fixture.Store.Clock, _fixture.Store.Options, _fixture.Jobs, _fixture.Connections);
        var payload = new PyDict().Set("enqueue_remux_jobs", true).Set("scan_trigger", "watcher").Set("media_scope", mediaType).Set("library_id", _libraryId);
        var job = await _fixture.Jobs.EnqueueOrGetAsync(
            $"scan-{Guid.NewGuid():N}", ProcessingWatchedFolderScanDispatchJobKinds.ScanDispatch, PyJsonWriter.Dumps(payload, PyJsonFormat.Compact));
        try
        {
            await scan.HandleAsync(new JobWorkContext(job.Id, job.JobKind, job.PayloadJson, "scan-owner"), CancellationToken.None);
        }
        finally
        {
            await _fixture.Store.Execute($"UPDATE jobs SET status = 'completed' WHERE id = {job.Id}");
        }
    }

    /// <summary>Move the clock on, so a look Weir queued for later is due.</summary>
    private void Advance(int minutes) => _fixture.Store.Clock.Set(_fixture.Store.Clock.GetUtcNow().AddMinutes(minutes));

    /// <summary>How many times ffmpeg read a file from start to finish.</summary>
    private int IntegrityReads() => _media.Calls.Count(argv => argv.Contains("-xerror"));

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

    private async Task ScanAndDrainAsync(string mediaType)
    {
        await ScanAsync(mediaType);
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

    private Task<long> RemuxJobsAsync() =>
        _fixture.Store.Scalar($"SELECT count(*) FROM jobs WHERE job_kind = '{RemuxPassOutcomes.JobKind}'");

    private async Task<string?> StatusAsync(string relative)
    {
        using var connection = _fixture.Store.Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM files WHERE relative_path = $p";
        command.Parameters.AddWithValue("$p", relative);
        return await command.ExecuteScalarAsync() is { } value and not DBNull ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
    }

    private async Task<string> ReasonAsync(string relative)
    {
        using var connection = _fixture.Store.Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status_reason FROM files WHERE relative_path = $p";
        command.Parameters.AddWithValue("$p", relative);
        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private async Task<DateTimeOffset?> HoldUntilAsync(string relative)
    {
        using var connection = _fixture.Store.Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT hold_until FROM files WHERE relative_path = $p";
        command.Parameters.AddWithValue("$p", relative);
        return PythonTimestamps.Parse(await command.ExecuteScalarAsync());
    }

    private Task<long> LeftActivityAsync() =>
        _fixture.Store.Scalar("SELECT count(*) FROM activity_events WHERE event_type = 'processing.file_left_watched_folder'");

    /// <summary>A row as a scan leaves it. <paramref name="lastSeenMinutesAgo"/> null is a row no scan has seen.</summary>
    private Task InsertRowAsync(string relative, string status, long size = 2000, int? lastSeenMinutesAgo = 60)
    {
        var lastSeen = lastSeenMinutesAgo is { } minutes
            ? "'" + _fixture.Store.Clock.GetUtcNow().AddMinutes(-minutes).ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture) + "'"
            : "NULL";
        return _fixture.Store.Execute(
            $"INSERT INTO files (library_id, relative_path, status, status_reason, size_bytes, last_seen_at) " +
            $"VALUES ({_libraryId}, '{relative}', '{status}', 'seeded', {size}, {lastSeen})");
    }

    // --- #644 ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_tv_episode_whose_season_cleanup_was_skipped_is_not_cleaned_again()
    {
        await SetUpAsync("tv");
        var episode = _folders.Source(Path.Join("Show.S01", "Show.S01E01.mkv"));

        await ScanAndDrainAsync("tv");

        Assert.Equal(1, await RemuxJobsAsync());
        Assert.True(File.Exists(episode), "no TV manager could be asked, so Weir left the season folder in place");

        // TV passes record no source size, so before #644 even this rescan, with the output still there, cleaned it again.
        await ScanAndDrainAsync("tv");
        Assert.Equal(1, await RemuxJobsAsync());

        // The manager imports it and the output goes: still finished.
        File.Delete(_folders.Out(Path.Join("Show.S01", "Show.S01E01.mkv")));
        await ScanAndDrainAsync("tv");
        await ScanAndDrainAsync("tv");

        Assert.Equal(1, await RemuxJobsAsync());
        Assert.Single(_media.Remuxes);
        Assert.Equal("processed", await StatusAsync("Show.S01/Show.S01E01.mkv"));
    }

    [Fact]
    public async Task A_movie_left_in_the_watched_root_is_not_cleaned_again_once_its_output_is_imported()
    {
        await SetUpAsync("movie");
        var source = _folders.Source("Film.2024.mkv");
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddDays(-3));

        await ScanAndDrainAsync("movie");
        Assert.Equal(1, await RemuxJobsAsync());
        Assert.True(File.Exists(source), "a file directly in the watched root has no release folder to remove");

        File.Delete(_folders.Out("Film.2024.mkv"));
        await ScanAndDrainAsync("movie");
        await ScanAndDrainAsync("movie");

        Assert.Equal(1, await RemuxJobsAsync());
        Assert.Single(_media.Remuxes);
        Assert.Equal("processed", await StatusAsync("Film.2024.mkv"));
    }

    [Fact]
    public async Task A_replaced_episode_is_still_cleaned_in_a_library_that_removes_originals()
    {
        await SetUpAsync("tv");
        var episode = _folders.Source(Path.Join("Show.S01", "Show.S01E01.mkv"));
        await ScanAndDrainAsync("tv");
        Assert.Equal(1, await RemuxJobsAsync());

        // A better release lands at the same path: a different size and a later time.
        File.WriteAllBytes(episode, Enumerable.Repeat((byte)'y', 3000).ToArray());
        File.SetLastWriteTimeUtc(episode, DateTime.UtcNow.AddMinutes(5));
        await ScanAndDrainAsync("tv");

        Assert.Equal(2, await RemuxJobsAsync());
        Assert.Equal("processed", await StatusAsync("Show.S01/Show.S01E01.mkv"));
    }

    [WindowsFact("Needs Windows' mandatory file locks.")]
    public async Task An_interrupted_movie_removal_is_finished_while_the_output_is_there_and_never_cleans_it_again()
    {
        await SetUpAsync("movie");
        var source = _folders.Source(Path.Join("Film.2024", "Film.2024.mkv"));
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddDays(-3));

        // Readable, so the pass can clean it, but not deletable: the release folder cannot be removed.
        using (new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await ScanAndDrainAsync("movie");
            Assert.Equal(1, await RemuxJobsAsync());
            Assert.True(File.Exists(source), "the lock stopped Weir removing the release folder");

            // Still locked: the removal waits, the file stays processed, and nothing is cleaned again.
            await ScanAndDrainAsync("movie");
            Assert.Equal(1, await RemuxJobsAsync());
            Assert.Equal("processed", await StatusAsync("Film.2024/Film.2024.mkv"));
        }

        // The lock clears and the output is still there: the next scan finishes the removal.
        await ScanAndDrainAsync("movie");

        Assert.Equal(1, await RemuxJobsAsync());
        Assert.False(Directory.Exists(Path.Join(_folders.Watched, "Film.2024")));
        Assert.Equal("processed", await StatusAsync("Film.2024/Film.2024.mkv"));
    }

    [WindowsFact("Needs Windows' mandatory file locks.")]
    public async Task An_interrupted_movie_removal_is_not_cleaned_again_after_the_output_is_imported()
    {
        await SetUpAsync("movie");
        var source = _folders.Source(Path.Join("Film.2024", "Film.2024.mkv"));
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddDays(-3));

        // Readable, so the pass can clean it, but not deletable: the release folder cannot be removed.
        using (new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await ScanAndDrainAsync("movie");
        }

        File.Delete(_folders.Out(Path.Join("Film.2024", "Film.2024.mkv")));
        await ScanAndDrainAsync("movie");
        await ScanAndDrainAsync("movie");

        Assert.Equal(1, await RemuxJobsAsync());
        Assert.True(File.Exists(source), "with no validated output left, Weir does not remove the original");
        Assert.Equal("processed", await StatusAsync("Film.2024/Film.2024.mkv"));
    }

    // --- #645 ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_held_file_that_leaves_the_watched_folder_is_forgotten_with_one_activity_entry()
    {
        await SetUpAsync("movie", minFileAgeSeconds: 3600);
        var source = _folders.Source(Path.Join("Film.2024", "Film.2024.mkv"));

        await ScanAsync("movie");
        Assert.Equal("on_hold", await StatusAsync("Film.2024/Film.2024.mkv"));

        File.Delete(source);
        await ScanAsync("movie");
        Assert.Equal("on_hold", await StatusAsync("Film.2024/Film.2024.mkv"));

        // Gone for longer than a download client takes to move a file.
        _fixture.Store.Clock.Set(_fixture.Store.Clock.GetUtcNow().AddMinutes(11));
        await ScanAsync("movie");

        Assert.Null(await StatusAsync("Film.2024/Film.2024.mkv"));
        Assert.Equal(1, await LeftActivityAsync());
        Assert.Equal(0, await RemuxJobsAsync());

        await ScanAsync("movie");
        Assert.Equal(1, await LeftActivityAsync());
    }

    [Fact]
    public async Task Waiting_and_failed_rows_for_files_that_are_gone_are_forgotten_but_outcomes_stay()
    {
        await SetUpAsync("movie");
        await InsertRowAsync("Gone/waiting.mkv", ProcessingFileStatuses.Unprocessed);
        await InsertRowAsync("Gone/failed.mkv", ProcessingFileStatuses.ProcessingFailed);
        await InsertRowAsync("Gone/outside.mkv", ProcessingFileStatuses.OutOfSchedule);
        await InsertRowAsync("Gone/processed.mkv", ProcessingFileStatuses.Processed);
        await InsertRowAsync("Gone/passed.mkv", ProcessingFileStatuses.PassedThrough);
        await InsertRowAsync("Gone/cancelled.mkv", ProcessingFileStatuses.Cancelled);

        await ScanAsync("movie");

        Assert.Null(await StatusAsync("Gone/waiting.mkv"));
        Assert.Null(await StatusAsync("Gone/failed.mkv"));
        Assert.Null(await StatusAsync("Gone/outside.mkv"));
        Assert.Equal("processed", await StatusAsync("Gone/processed.mkv"));
        Assert.Equal("passed_through", await StatusAsync("Gone/passed.mkv"));
        Assert.Equal("cancelled", await StatusAsync("Gone/cancelled.mkv"));
        Assert.Equal(3, await LeftActivityAsync());
    }

    [Fact]
    public async Task A_row_seen_moments_ago_or_never_seen_is_kept()
    {
        await SetUpAsync("movie");
        await InsertRowAsync("Gone/just-seen.mkv", ProcessingFileStatuses.Unprocessed, lastSeenMinutesAgo: 2);
        await InsertRowAsync("Gone/never-seen.mkv", ProcessingFileStatuses.Unprocessed, lastSeenMinutesAgo: null);

        await ScanAsync("movie");

        Assert.Equal("unprocessed", await StatusAsync("Gone/just-seen.mkv"));
        Assert.Equal("unprocessed", await StatusAsync("Gone/never-seen.mkv"));
        Assert.Equal(0, await LeftActivityAsync());
    }

    [Fact]
    public async Task A_file_with_a_pass_queued_is_left_to_that_pass()
    {
        await SetUpAsync("movie");
        await InsertRowAsync("Gone/queued.mkv", ProcessingFileStatuses.Unprocessed);
        var payload = $"{{\"relative_media_path\":\"Gone/queued.mkv\",\"media_scope\":\"movie\",\"library_id\":{_libraryId}}}";
        await _fixture.Jobs.EnqueueOrGetAsync($"queued-{Guid.NewGuid():N}", RemuxPassOutcomes.JobKind, payload);

        await ScanAsync("movie");

        Assert.Equal("unprocessed", await StatusAsync("Gone/queued.mkv"));
        Assert.Equal(0, await LeftActivityAsync());
    }

    [Fact]
    public async Task Nothing_is_forgotten_when_the_watched_folder_itself_is_missing()
    {
        await SetUpAsync("movie");
        await InsertRowAsync("Gone/waiting.mkv", ProcessingFileStatuses.Unprocessed);
        Directory.Delete(_folders.Watched, recursive: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ScanAsync("movie"));

        Assert.Equal("unprocessed", await StatusAsync("Gone/waiting.mkv"));
        Assert.Equal(0, await LeftActivityAsync());
    }

    // --- #646 ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_file_that_will_not_read_to_the_end_is_looked_at_again_later_not_on_every_scan()
    {
        await SetUpAsync("movie");
        _media.IntegrityError = "incomplete media data";
        _folders.Source(Path.Join("Film.2024", "Film.2024.mkv"));

        await ScanAndDrainAsync("movie");

        Assert.Equal("on_hold", await StatusAsync("Film.2024/Film.2024.mkv"));
        Assert.Equal(1, IntegrityReads());
        Assert.Equal(2, await RemuxJobsAsync());

        // Scans meanwhile change nothing: the next look is already queued, for later.
        await ScanAndDrainAsync("movie");
        await ScanAndDrainAsync("movie");

        Assert.Equal(1, IntegrityReads());
        Assert.Equal(2, await RemuxJobsAsync());
    }

    [Fact]
    public async Task A_file_waiting_for_its_next_look_stays_on_hold_until_then_and_keeps_saying_why()
    {
        await SetUpAsync("movie");
        _media.IntegrityError = "incomplete media data";
        _folders.Source(Path.Join("Film.2024", "Film.2024.mkv"));

        await ScanAndDrainAsync("movie");
        var reason = await ReasonAsync("Film.2024/Film.2024.mkv");
        var booked = await HoldUntilAsync("Film.2024/Film.2024.mkv");

        // The look is booked for five minutes from now, and the file says so.
        Assert.NotNull(booked);
        var expected = _fixture.Store.Clock.GetUtcNow().AddMinutes(RemuxPassHandler.UnreadableWaitMinutes[0]);
        Assert.InRange(booked.Value, expected.AddSeconds(-2), expected.AddSeconds(2));

        // Scans meanwhile find it present and settled. It is still not ready for the next free lane: calling it ready put it
        // first in line on Processing while a lane stood empty.
        await ScanAndDrainAsync("movie");
        await ScanAndDrainAsync("movie");

        Assert.Equal("on_hold", await StatusAsync("Film.2024/Film.2024.mkv"));
        Assert.Equal(reason, await ReasonAsync("Film.2024/Film.2024.mkv"));
        Assert.Equal(booked, await HoldUntilAsync("Film.2024/Film.2024.mkv"));
    }

    [Fact]
    public async Task A_file_that_never_reads_to_the_end_stops_being_waited_for_and_the_failure_policy_runs()
    {
        await SetUpAsync("movie");
        _media.IntegrityError = "incomplete media data";
        _folders.Source(Path.Join("Film.2024", "Film.2024.mkv"));

        await ScanAndDrainAsync("movie");
        foreach (var minutes in RemuxPassHandler.UnreadableWaitMinutes)
        {
            Assert.Equal("on_hold", await StatusAsync("Film.2024/Film.2024.mkv"));
            Advance(minutes + 1);
            await DrainAsync();
        }

        Assert.Equal(RemuxPassHandler.UnreadableWaitMinutes.Count + 1, IntegrityReads());
        Assert.Equal("processing_failed", await StatusAsync("Film.2024/Film.2024.mkv"));
        Assert.Contains(
            "damaged or incomplete rather than still arriving",
            await ReasonAsync("Film.2024/Film.2024.mkv"),
            StringComparison.Ordinal);
        // The library hands the original back rather than keeping it (its failure policy).
        Assert.Equal(1, await _fixture.Store.Scalar("SELECT count(*) FROM jobs WHERE job_kind = 'processing.file.pass_through.v1'"));
    }

    [Fact]
    public async Task A_file_that_is_still_growing_keeps_being_waited_for()
    {
        await SetUpAsync("movie");
        _media.IntegrityError = "incomplete media data";
        var source = _folders.Source(Path.Join("Film.2024", "Film.2024.mkv"));

        await ScanAndDrainAsync("movie");
        for (var i = 0; i < RemuxPassHandler.UnreadableWaitMinutes.Count + 2; i++)
        {
            await File.AppendAllTextAsync(source, "more");
            Advance(60);
            await DrainAsync();
            Assert.Equal("on_hold", await StatusAsync("Film.2024/Film.2024.mkv"));
        }

        Assert.Equal(0, await _fixture.Store.Scalar("SELECT count(*) FROM jobs WHERE job_kind = 'processing.file.pass_through.v1'"));
    }

    // --- #643 ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_cancelled_file_is_left_alone_until_it_changes()
    {
        await SetUpAsync("movie");
        var source = _folders.Source(Path.Join("Film.2024", "Film.2024.mkv"));
        await InsertRowAsync("Film.2024/Film.2024.mkv", ProcessingFileStatuses.Cancelled, new FileInfo(source).Length);

        await ScanAndDrainAsync("movie");
        await ScanAndDrainAsync("movie");

        Assert.Equal(0, await RemuxJobsAsync());
        Assert.Equal("cancelled", await StatusAsync("Film.2024/Film.2024.mkv"));

        // A new download at the same path is a new file.
        File.WriteAllBytes(source, Enumerable.Repeat((byte)'y', 3000).ToArray());
        await ScanAndDrainAsync("movie");

        Assert.Equal(1, await RemuxJobsAsync());
    }
}
