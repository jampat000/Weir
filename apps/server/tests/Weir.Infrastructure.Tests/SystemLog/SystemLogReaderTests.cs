using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.Logs;
using Weir.Infrastructure.Logging;
using Weir.Infrastructure.SystemLog;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.SystemLog;

/// <summary>System › Logs read from a real database and a real log file: one list, filtered, counted and paged.</summary>
public sealed class SystemLogReaderTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly StoreFixture _store = new();
    private readonly WeirLogFile _logFile;
    private readonly SystemLogReader _reader;
    private long _lastJob;

    public SystemLogReaderTests()
    {
        _logFile = new WeirLogFile(_store.Home.Join("logs", "weir.log"), new FakeTimeProvider(Noon));
        _reader = new SystemLogReader(_logFile, NullLogger<SystemLogReader>.Instance);
    }

    public void Dispose()
    {
        _logFile.Dispose();
        _store.Dispose();
    }

    [Fact]
    public async Task Events_jobs_and_server_lines_are_one_list_newest_first()
    {
        await AddEvent(minutesAgo: 30, "auth.login_succeeded", "Signed in", result: "success");
        await AddJob(minutesAgo: 20, "processing.file.remux_pass.v1", "completed");
        AddServerLine(minutesAgo: 10, LogLevel.Warning, "weir.platform.suite_settings.backups", "The backup folder is nearly full");

        var page = await Read(new SystemLogFilter());

        Assert.Equal(["server", "job", "event"], page.Rows.Select(row => SystemLogSources.NameOf(row.Source)));
        Assert.Equal(
            [SystemLogCategories.Backups, SystemLogCategories.Processing, SystemLogCategories.SignIn],
            page.Rows.Select(row => row.Category));
        Assert.Equal([SystemLogLevels.Warning, SystemLogLevels.Success, SystemLogLevels.Success], page.Rows.Select(row => row.Level));
        Assert.Equal(3, page.Total);
    }

    [Fact]
    public async Task An_event_about_one_file_is_left_to_activity()
    {
        await AddEvent(minutesAgo: 5, "processing.file_remux_pass_completed", "Heat processed", result: "success", relativePath: "Heat/heat.mkv");
        await AddEvent(minutesAgo: 6, "auth.login_succeeded", "Signed in", result: "success");

        var page = await Read(new SystemLogFilter());

        Assert.Equal(["Signed in"], page.Rows.Select(row => row.Title));
    }

    [Fact]
    public async Task A_page_ends_where_the_cursor_says_and_the_next_one_continues_without_a_gap_or_a_repeat()
    {
        // Several rows share an instant, across sources, so the page edge falls inside a tie.
        for (var index = 0; index < 7; index++)
        {
            await AddEvent(minutesAgo: 10 + (index / 3), "auth.login_succeeded", $"Event {index}", result: "success");
            await AddJob(minutesAgo: 10 + (index / 3), "processing.file.remux_pass.v1", "completed");
            AddServerLine(minutesAgo: 10 + (index / 3), LogLevel.Warning, "weir.processing", $"Line {index}");
        }

        var seen = new List<string>();
        SystemLogPosition? after = null;
        var pages = 0;
        do
        {
            var page = await Read(new SystemLogFilter(), after, limit: 4);
            seen.AddRange(page.Rows.Select(row => row.Id));
            after = SystemLogCursor.TryDecode(page.NextCursor, out var next) ? next : null;
            pages++;
        }
        while (after is not null && pages < 20);

        Assert.Equal(21, seen.Count);
        Assert.Equal(21, seen.Distinct().Count());
        Assert.Equal(6, pages);
    }

    [Fact]
    public async Task Choosing_a_level_keeps_only_rows_of_that_level_from_every_source()
    {
        await AddEvent(minutesAgo: 5, "auth.login_failed", "Sign-in failed", result: "failed");
        await AddEvent(minutesAgo: 6, "auth.login_succeeded", "Signed in", result: "success");
        await AddJob(minutesAgo: 7, "processing.file.remux_pass.v1", "failed");
        await AddJob(minutesAgo: 8, "processing.file.remux_pass.v1", "completed");
        AddServerLine(minutesAgo: 9, LogLevel.Error, "weir.processing", "The pass stopped");
        AddServerLine(minutesAgo: 10, LogLevel.Information, "weir.processing", "A pass started");

        var page = await Read(new SystemLogFilter { Levels = [SystemLogLevels.Error] });

        Assert.Equal(["event", "job", "server"], page.Rows.Select(row => SystemLogSources.NameOf(row.Source)));
        Assert.All(page.Rows, row => Assert.Equal(SystemLogLevels.Error, row.Level));
        Assert.Equal(3, page.Total);
    }

    [Fact]
    public async Task Each_count_leaves_out_the_filter_it_counts_so_a_chip_shows_what_choosing_it_would_give()
    {
        await AddEvent(minutesAgo: 5, "auth.login_failed", "Sign-in failed", result: "failed");
        await AddEvent(minutesAgo: 6, "library.scan_completed", "Scan finished", result: "success");
        await AddJob(minutesAgo: 7, "processing.file.remux_pass.v1", "failed");
        AddServerLine(minutesAgo: 8, LogLevel.Error, "weir.platform.auth.router", "Too many attempts");
        AddServerLine(minutesAgo: 9, LogLevel.Warning, "weir.processing", "Slow disk");

        var page = await Read(new SystemLogFilter { Sources = [SystemLogSource.Event], Levels = [SystemLogLevels.Error] });

        Assert.Equal(1, page.Total);
        Assert.Equal(1, page.Counts.BySource[SystemLogSources.Event]);
        Assert.Equal(1, page.Counts.BySource[SystemLogSources.Job]);
        Assert.Equal(1, page.Counts.BySource[SystemLogSources.Server]);
        Assert.Equal(1, page.Counts.ByLevel[SystemLogLevels.Error]);
        Assert.Equal(1, page.Counts.ByLevel[SystemLogLevels.Success]);
        Assert.Equal(1, page.Counts.ByCategory[SystemLogCategories.SignIn]);
        Assert.Equal(0, page.Counts.ByCategory[SystemLogCategories.Scans]);
    }

    [Fact]
    public async Task A_category_filter_keeps_rows_of_those_categories_in_every_source()
    {
        await AddEvent(minutesAgo: 5, "auth.login_succeeded", "Signed in", result: "success");
        await AddEvent(minutesAgo: 6, "processing.work_temp_stale_sweep_completed", "Temporary files cleared", result: "success");
        await AddJob(minutesAgo: 7, "processing.work_temp_stale_sweep.v1", "completed");
        await AddJob(minutesAgo: 8, "processing.file.remux_pass.v1", "completed");
        AddServerLine(minutesAgo: 9, LogLevel.Warning, "Weir.Infrastructure.Jobs.WorkTempStaleSweepHandler", "A file could not be removed");

        var page = await Read(new SystemLogFilter { Categories = [SystemLogCategories.Cleanup] });

        Assert.Equal(3, page.Rows.Count);
        Assert.All(page.Rows, row => Assert.Equal(SystemLogCategories.Cleanup, row.Category));
    }

    [Fact]
    public async Task A_workflow_filter_keeps_that_workflows_events_and_jobs_and_leaves_out_the_server_log()
    {
        await AddEvent(minutesAgo: 5, "library.scan_completed", "Movies scanned", result: "success", libraryId: 1);
        await AddEvent(minutesAgo: 6, "library.scan_completed", "Shows scanned", result: "success", libraryId: 2);
        await AddJob(minutesAgo: 7, "processing.library.scan.v1", "completed", payload: "{\"library_id\": 1}");
        await AddJob(minutesAgo: 8, "processing.library.scan.v1", "completed", payload: "{\"library_id\": 2}");
        AddServerLine(minutesAgo: 9, LogLevel.Warning, "weir.library_mode.router", "A library is slow");

        var page = await Read(new SystemLogFilter { WorkflowId = 1 });

        Assert.Equal(["Movies scanned", "Finished"], page.Rows.Select(row => row.Title));
        Assert.All(page.Rows, row => Assert.Equal(1, row.WorkflowId));
        Assert.Equal(0, page.Counts.BySource[SystemLogSources.Server]);
    }

    [Fact]
    public async Task A_search_finds_a_job_by_the_words_of_its_kind_and_its_status()
    {
        await AddJob(minutesAgo: 5, "processing.file.remux_pass.v1", "leased");
        await AddJob(minutesAgo: 6, "processing.file.remux_pass.v1", "completed");
        await AddJob(minutesAgo: 7, "processing.work_temp_stale_sweep.v1", "failed", lastError: "Disk full");

        Assert.Single((await Read(new SystemLogFilter { Text = "running" })).Rows);
        Assert.Single((await Read(new SystemLogFilter { Text = "temporary" })).Rows);
        Assert.Single((await Read(new SystemLogFilter { Text = "disk full" })).Rows);
        Assert.Equal(3, (await Read(new SystemLogFilter { Text = "" })).Rows.Count);
    }

    [Fact]
    public async Task A_search_finds_events_and_server_lines_by_their_text()
    {
        await AddEvent(minutesAgo: 5, "auth.login_failed", "Sign-in failed for Alice", result: "failed");
        await AddEvent(minutesAgo: 6, "auth.login_succeeded", "Signed in", result: "success");
        AddServerLine(minutesAgo: 7, LogLevel.Warning, "weir.processing", "Alice's folder is missing");

        var page = await Read(new SystemLogFilter { Text = "alice" });

        Assert.Equal(["event", "server"], page.Rows.Select(row => SystemLogSources.NameOf(row.Source)));
        Assert.Equal(2, page.Total);
    }

    [Fact]
    public async Task A_time_range_keeps_rows_inside_it_in_every_source()
    {
        await AddEvent(minutesAgo: 5, "auth.login_succeeded", "Recent", result: "success");
        await AddEvent(minutesAgo: 500, "auth.login_succeeded", "Old", result: "success");
        await AddJob(minutesAgo: 6, "processing.file.remux_pass.v1", "completed");
        await AddJob(minutesAgo: 500, "processing.file.remux_pass.v1", "completed");
        AddServerLine(minutesAgo: 7, LogLevel.Warning, "weir.processing", "Recent line");
        AddServerLine(minutesAgo: 500, LogLevel.Warning, "weir.processing", "Old line");

        var page = await Read(new SystemLogFilter { From = Noon.AddMinutes(-60), To = Noon });

        Assert.Equal(3, page.Rows.Count);
        Assert.All(page.Rows, row => Assert.True(row.At >= Noon.AddMinutes(-60)));
    }

    [Fact]
    public async Task A_filter_only_events_have_leaves_out_jobs_and_server_lines_and_counts_them_as_none()
    {
        await AddEvent(minutesAgo: 5, "auth.login_succeeded", "Signed in", result: "success", trigger: "manual");
        await AddEvent(minutesAgo: 6, "library.scan_completed", "Scan finished", result: "success", trigger: "scheduled");
        await AddJob(minutesAgo: 7, "processing.file.remux_pass.v1", "completed");
        AddServerLine(minutesAgo: 8, LogLevel.Warning, "weir.processing", "A line");

        var page = await Read(new SystemLogFilter { Trigger = "manual" });

        Assert.Equal(["Signed in"], page.Rows.Select(row => row.Title));
        Assert.Equal(0, page.Counts.BySource[SystemLogSources.Job]);
        Assert.Equal(0, page.Counts.BySource[SystemLogSources.Server]);
    }

    [Fact]
    public async Task Only_the_job_statuses_asked_for_are_listed_and_a_routine_finished_scan_is_otherwise_left_out()
    {
        await AddJob(minutesAgo: 5, "processing.watched_folder.remux_scan_dispatch.v1", "completed");
        await AddJob(minutesAgo: 6, "processing.watched_folder.remux_scan_dispatch.v1", "failed", lastError: "Folder missing");
        await AddJob(minutesAgo: 7, "processing.file.remux_pass.v1", "pending");

        var everything = await Read(new SystemLogFilter());
        var completed = await Read(new SystemLogFilter { JobStatuses = [ProcessingJobStatus.Completed] });
        var queued = await Read(new SystemLogFilter { JobStatuses = [ProcessingJobStatus.Pending, ProcessingJobStatus.Failed] });

        Assert.Equal(2, everything.Rows.Count);
        Assert.Single(completed.Rows);
        Assert.Equal(2, queued.Rows.Count);
    }

    [Fact]
    public async Task Everything_about_one_job_is_its_row_the_events_that_name_it_and_the_lines_written_while_it_ran()
    {
        var job = await AddJob(minutesAgo: 5, "processing.file.remux_pass.v1", "failed", lastError: "ffmpeg stopped");
        await AddJob(minutesAgo: 6, "processing.file.remux_pass.v1", "completed");
        await AddEvent(minutesAgo: 4, "processing.worker_failure", "A Weir job stopped with an error", result: "failed", detail: $"{{\"job_id\": {job}}}");
        await AddEvent(minutesAgo: 3, "processing.worker_failure", "Another job stopped", result: "failed", detail: "{\"job_id\": 99999}");
        AddServerLine(minutesAgo: 4, LogLevel.Error, "weir.processing", "ffmpeg stopped", jobId: job.ToString(CultureInfo.InvariantCulture));
        AddServerLine(minutesAgo: 4, LogLevel.Error, "weir.processing", "Unrelated", jobId: "99999");

        var page = await Read(new SystemLogFilter { JobId = job });

        Assert.Equal(3, page.Rows.Count);
        Assert.Equal(["event", "job", "server"], page.Rows.Select(row => SystemLogSources.NameOf(row.Source)).Order());
    }

    [Fact]
    public async Task A_server_line_with_an_exception_can_be_asked_for_alone()
    {
        AddServerLine(minutesAgo: 5, LogLevel.Error, "weir.processing", "It broke", exception: new InvalidOperationException("The pass broke."));
        AddServerLine(minutesAgo: 6, LogLevel.Warning, "weir.processing", "It is slow");

        var withException = await Read(new SystemLogFilter { HasException = true });

        Assert.Equal(["It broke"], withException.Rows.Select(row => row.Title));
        Assert.Contains("The pass broke.", withException.Rows[0].Detail);
    }

    [Fact]
    public async Task A_row_carries_its_sources_own_record_for_the_screen_to_open()
    {
        await AddEvent(minutesAgo: 5, "auth.login_failed", "Sign-in failed", result: "failed", trigger: "manual", detail: "alice");
        var job = await AddJob(minutesAgo: 6, "processing.file.remux_pass.v1", "failed", lastError: "ffmpeg stopped", payload: "{\"relative_media_path\": \"Heat/heat.mkv\", \"library_id\": 1}");
        AddServerLine(minutesAgo: 7, LogLevel.Warning, "weir.processing", "It is slow");

        var page = await Read(new SystemLogFilter());

        var eventRecord = page.Rows[0].Record;
        Assert.Equal("auth.login_failed", WireConvert.Str(eventRecord["event_type"]));
        Assert.Equal("manual", WireConvert.Str(eventRecord["trigger"]));
        var jobRecord = page.Rows[1].Record;
        Assert.Equal(job, (long)((WireInteger)jobRecord["id"]).Value);
        Assert.Equal("ffmpeg stopped", WireConvert.Str(jobRecord["last_error"]));
        Assert.Equal("Couldn't finish this job for heat.mkv", page.Rows[1].Title);
        Assert.Equal("Process a media file · attempt 1 of 3", page.Rows[1].Detail);
        Assert.Equal("weir.processing", WireConvert.Str(page.Rows[2].Record["logger"]));
    }

    [Fact]
    public async Task A_level_in_the_query_and_the_level_a_row_shows_always_agree()
    {
        var resultsAndStatuses = new[] { "failed", "warning", "retrying", "success", "skipped", "running" };
        foreach (var result in resultsAndStatuses)
        {
            await AddEvent(minutesAgo: 5, "processing.handoff_reported", $"Event {result}", result: result);
        }

        foreach (var status in new[] { "pending", "leased", "completed", "failed", "cancelled", "handler_ok_finalize_failed" })
        {
            await AddJob(minutesAgo: 5, "processing.library.clean.v1", status, lastError: status == "pending" ? "Try again" : null);
        }

        foreach (var level in SystemLogLevels.All)
        {
            var page = await Read(new SystemLogFilter { Levels = [level] }, limit: 50);

            Assert.All(page.Rows, row => Assert.Equal(level, row.Level));
            Assert.Equal(page.Rows.Count, page.Counts.ByLevel[level]);
        }
    }

    [Fact]
    public async Task A_category_in_the_query_and_the_category_a_row_shows_always_agree()
    {
        foreach (var eventType in SystemLogRules.EventTypeCategories.Select(rule => rule.Prefix + "example").Append("something.new"))
        {
            await AddEvent(minutesAgo: 5, eventType, eventType, result: "success");
        }

        foreach (var kind in SystemLogRules.JobKinds.Select(rule => rule.Kind).Append("processing.other.v1"))
        {
            await AddJob(minutesAgo: 5, kind, "completed");
        }

        foreach (var category in SystemLogCategories.All)
        {
            var page = await Read(new SystemLogFilter { Categories = [category] }, limit: 100);

            Assert.All(page.Rows, row => Assert.Equal(category, row.Category));
            Assert.Equal(page.Rows.Count, page.Counts.ByCategory[category]);
        }
    }

    [Fact]
    public async Task Workflow_names_are_looked_up_for_the_ids_given_and_a_deleted_workflow_has_none()
    {
        var ids = await _store.WithUnitOfWork(uow => uow.QueryAsync("SELECT id FROM libraries ORDER BY id LIMIT 1", reader => reader.GetInt64(0)));
        var names = await _store.WithUnitOfWork(uow => SystemLogReader.WorkflowNamesAsync(uow, [ids[0], 424242]));

        Assert.Single(names);
        Assert.False(string.IsNullOrEmpty(names[ids[0]]));
    }

    private Task<SystemLogPage> Read(SystemLogFilter filter, SystemLogPosition? after = null, int limit = 50) =>
        _store.WithUnitOfWork(uow => _reader.ReadAsync(uow, filter, after, limit), commit: false);

    private Task<int> AddEvent(
        int minutesAgo,
        string eventType,
        string title,
        string? result,
        string? trigger = null,
        long? libraryId = null,
        string? relativePath = null,
        string? detail = null) =>
        _store.WithUnitOfWork(uow => uow.ExecuteAsync(
            "INSERT INTO activity_events (created_at, event_type, module, title, detail, \"trigger\", result, library_id, relative_path) " +
            "VALUES (@at, @type, 'processing', @title, @detail, @trigger, @result, @library, @path)",
            ("@at", StoredAt(minutesAgo)),
            ("@type", eventType),
            ("@title", title),
            ("@detail", detail),
            ("@trigger", trigger),
            ("@result", result),
            ("@library", libraryId),
            ("@path", relativePath)));

    private async Task<long> AddJob(int minutesAgo, string kind, string status, string? lastError = null, string? payload = null)
    {
        var id = ++_lastJob;
        await _store.WithUnitOfWork(uow => uow.ExecuteAsync(
            "INSERT INTO jobs (id, dedupe_key, job_kind, payload_json, status, attempt_count, max_attempts, last_error, created_at, updated_at) " +
            "VALUES (@id, @key, @kind, @payload, @status, 1, 3, @error, @at, @at)",
            ("@id", id),
            ("@key", $"test:{id}"),
            ("@kind", kind),
            ("@payload", payload),
            ("@status", status),
            ("@error", lastError),
            ("@at", StoredAt(minutesAgo))));
        return id;
    }

    private void AddServerLine(int minutesAgo, LogLevel level, string logger, string message, string? jobId = null, Exception? exception = null) =>
        _logFile.WriteLine(LogLineFormat.JsonLine(Noon.AddMinutes(-minutesAgo), level, logger, message, exception, null, jobId));

    private static string StoredAt(int minutesAgo) => Noon.AddMinutes(-minutesAgo).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
}
