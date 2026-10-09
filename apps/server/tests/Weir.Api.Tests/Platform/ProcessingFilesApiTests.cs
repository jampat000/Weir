using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Weir.Core.Activity;
using Weir.Core.Processing.RemuxPass;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Processing.RemuxPass;

namespace Weir.Api.Tests.Platform;

/// <summary>
/// End-to-end proof of the fix for #530 at the HTTP layer: <c>GET /processing/files</c> must answer 200 for a
/// row at <c>passed_through</c> or <c>rejected</c>, and the <c>file_status</c> query filter must accept
/// either value rather than 422. Also covers the everyday Processing Files list shape.
/// </summary>
public sealed class ProcessingFilesApiTests
{
    private static async Task<long> SeedLibraryAsync(WeirTestServer server, string name = "Movies bug530") =>
        await TestDatabase.ScalarAsync(
            server,
            "INSERT INTO libraries (name, media_type) VALUES ($name, 'movie') RETURNING id",
            ("$name", name));

    private static Task SeedFileAsync(WeirTestServer server, long libraryId, string relativePath, string status) =>
        TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO files (library_id, relative_path, status, last_seen_at) VALUES ($lib, $path, $status, CURRENT_TIMESTAMP)",
            ("$lib", libraryId), ("$path", relativePath), ("$status", status));

    private static Task<long> SeedFileWithIdAsync(WeirTestServer server, long libraryId, string relativePath, string status) =>
        TestDatabase.ScalarAsync(
            server,
            "INSERT INTO files (library_id, relative_path, status, last_seen_at) VALUES ($lib, $path, $status, CURRENT_TIMESTAMP) RETURNING id",
            ("$lib", libraryId), ("$path", relativePath), ("$status", status));

    [Theory]
    [InlineData("passed_through")]
    [InlineData("rejected")]
    public async Task Filtering_by_passed_through_or_rejected_answers_200_not_422(string status)
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var libraryId = await SeedLibraryAsync(server);
        await SeedFileAsync(server, libraryId, "Movie (2020)/movie.mkv", status);

        using var response = await client.GetAsync($"/api/v1/processing/files?file_status={status}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiTestClient.Json(response);
        var files = body!["files"]!.AsArray();
        var file = Assert.Single(files);
        Assert.Equal(status, file!["status"]!.GetValue<string>());
        Assert.Equal(1, body["status_counts"]![status]!.GetValue<int>());
    }

    [Fact]
    public async Task A_page_with_a_passed_through_file_alongside_others_answers_200_not_500()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var libraryId = await SeedLibraryAsync(server);
        await SeedFileAsync(server, libraryId, "a.mkv", "passed_through");
        await SeedFileAsync(server, libraryId, "b.mkv", "unprocessed");

        using var response = await client.GetAsync("/api/v1/processing/files");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiTestClient.Json(response);
        Assert.Equal(2, body!["returned"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_comma_separated_file_status_lists_every_row_in_any_of_them()
    {
        // The Processing screen asks for its "everything currently running" page this way (#781): one status
        // today, but the filter takes several.
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var libraryId = await SeedLibraryAsync(server);
        await SeedFileAsync(server, libraryId, "a.mkv", "processing");
        await SeedFileAsync(server, libraryId, "b.mkv", "unprocessed");
        await SeedFileAsync(server, libraryId, "c.mkv", "processed");

        using var response = await client.GetAsync("/api/v1/processing/files?file_status=processing,unprocessed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiTestClient.Json(response);
        var statuses = body!["files"]!.AsArray().Select(file => file!["status"]!.GetValue<string>()).Order(StringComparer.Ordinal);
        Assert.Equal(["processing", "unprocessed"], statuses);
    }

    [Fact]
    public async Task Only_a_file_held_for_being_gone_says_its_source_is_gone()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var libraryId = await SeedLibraryAsync(server);
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO files (library_id, relative_path, status, status_reason) VALUES ($lib, 'Film/gone.mkv', 'on_hold', $reason), ($lib, 'Film/settling.mkv', 'on_hold', 'Weir is confirming that nothing is still writing to this file.')",
            ("$lib", libraryId), ("$reason", GoneSourceText.HeldReason));

        using var response = await client.GetAsync("/api/v1/processing/files");

        var body = await ApiTestClient.Json(response);
        var files = body!["files"]!.AsArray().ToDictionary(file => file!["relative_path"]!.GetValue<string>());
        Assert.True(files["Film/gone.mkv"]!["source_gone"]!.GetValue<bool>());
        Assert.False(files["Film/settling.mkv"]!["source_gone"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_file_being_worked_on_carries_the_stage_its_pass_is_on_and_others_carry_none()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var libraryId = await SeedLibraryAsync(server);
        await SeedFileAsync(server, libraryId, "Film/running.mkv", "processing");
        await SeedFileAsync(server, libraryId, "Film/waiting.mkv", "unprocessed");
        server.Services.GetRequiredService<LiveProgressStore>().Update(
            "Film/running.mkv",
            new LiveProgress(null, "Weir is working out which tracks to keep.", null, "processing", null, null, [], [], PassStages.Planning));

        using var response = await client.GetAsync("/api/v1/processing/files");

        var body = await ApiTestClient.Json(response);
        var files = body!["files"]!.AsArray().ToDictionary(file => file!["relative_path"]!.GetValue<string>());
        Assert.Equal("planning", files["Film/running.mkv"]!["progress_stage"]!.GetValue<string>());
        Assert.Null(files["Film/waiting.mkv"]!["progress_stage"]);
    }

    [Fact]
    public async Task An_invalid_file_status_is_still_rejected_with_422()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();

        using var response = await client.GetAsync("/api/v1/processing/files?file_status=not_a_real_status");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_library_then_listing_it_round_trips_over_http()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();

        using var created = await client.PostAsync(
            "/api/v1/processing/libraries",
            new { csrf_token = await client.CsrfAsync(), name = "Anime", media_type = "movie", watched_folder = @"c:\anime-in", output_folder = @"c:\anime-out" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = await ApiTestClient.Json(created);
        Assert.Equal("Anime", createdBody!["name"]!.GetValue<string>());
        Assert.Equal("no_upstream_signal", createdBody["manager_coverage"]!.GetValue<string>());

        using var list = await client.GetAsync("/api/v1/processing/libraries");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var libraries = (await ApiTestClient.Json(list))!.AsArray();
        Assert.Contains(libraries, lib => lib!["name"]!.GetValue<string>() == "Anime");
    }

    [Fact]
    public async Task Two_libraries_with_overlapping_folders_are_refused_with_400()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var csrf = await client.CsrfAsync();
        await client.PostAsync("/api/v1/processing/libraries", new { csrf_token = csrf, name = "Anime", media_type = "movie", watched_folder = @"c:\anime-in", output_folder = @"c:\anime-out" });

        using var response = await client.PostAsync(
            "/api/v1/processing/libraries",
            new { csrf_token = await client.CsrfAsync(), name = "Anime 4K", media_type = "movie", watched_folder = @"c:\anime-out", output_folder = @"c:\anime4k-out" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("overlap", await ApiTestClient.Detail(response), StringComparison.Ordinal);
    }

    /// <summary>
    /// Once an owner removes a finished or failed title from Activity (the <c>DELETE /processing/files/{id}</c>
    /// "Remove from list" action), Processing must stop reporting it as current work — its failed-jobs alert and
    /// its "Just finished" list — while System's own Activity log and Jobs list, which read the same rows without
    /// asking for that, keep the complete record. Nothing about the file is deleted beyond the <c>files</c> row
    /// itself: not its Activity events, not its job row, and not the job row's dedupe key, which still refuses a
    /// second pass for the same file.
    /// </summary>
    [Fact]
    public async Task Removing_a_file_through_historys_own_endpoint_clears_it_from_processings_live_reports_but_not_from_logs()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var libraryId = await SeedLibraryAsync(server);

        var finishedId = await SeedFileWithIdAsync(server, libraryId, "Heat/heat.mkv", "processed");
        var failedId = await SeedFileWithIdAsync(server, libraryId, "Up/up.mkv", "processing_failed");

        var writer = server.Services.GetRequiredService<IActivityWriter>();
        await writer.RecordAsync(new ActivityEventDraft(
            ActivityEventTypes.ProcessingFileRemuxPassCompleted,
            "processing",
            "Heat finished",
            $"{{\"library_id\": {libraryId}, \"relative_media_path\": \"Heat/heat.mkv\", \"outcome\": \"live_output_written\"}}"));
        var dedupeKey = $"processing.file.remux_pass.v1:{libraryId}:Up/up.mkv";
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status) VALUES ($dedupe, 'processing.file.remux_pass.v1', $payload, 'failed')",
            ("$dedupe", dedupeKey), ("$payload", $"{{\"relative_media_path\": \"Up/up.mkv\", \"library_id\": {libraryId}}}"));

        // Before removal: the live alert and "just finished" list both name the file, the same as an unfiltered
        // System listing.
        using var beforeJobsAlert = await client.GetAsync("/api/v1/processing/jobs/inspection?status=failed&known_files_only=true");
        Assert.Single((await ApiTestClient.Json(beforeJobsAlert))!["jobs"]!.AsArray());
        using var beforeFinished = await client.GetAsync("/api/v1/activity/recent?event_type=" + ActivityEventTypes.ProcessingFileRemuxPassCompleted + "&known_files_only=true");
        Assert.Single((await ApiTestClient.Json(beforeFinished))!["items"]!.AsArray());

        foreach (var id in new[] { finishedId, failedId })
        {
            using var removed = await client.SendAsync(HttpMethod.Delete, $"/api/v1/processing/files/{id}", new { csrf_token = await client.CsrfAsync() });
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

        // After removal: the live alert and "just finished" list stop naming the forgotten file.
        using var afterJobsAlert = await client.GetAsync("/api/v1/processing/jobs/inspection?status=failed&known_files_only=true");
        Assert.Empty((await ApiTestClient.Json(afterJobsAlert))!["jobs"]!.AsArray());
        using var afterFinished = await client.GetAsync("/api/v1/activity/recent?event_type=" + ActivityEventTypes.ProcessingFileRemuxPassCompleted + "&known_files_only=true");
        Assert.Empty((await ApiTestClient.Json(afterFinished))!["items"]!.AsArray());

        // System's own Activity log and Jobs list, which never ask for known_files_only, still have the full record.
        using var logsAfter = await client.GetAsync("/api/v1/activity/recent?event_type=" + ActivityEventTypes.ProcessingFileRemuxPassCompleted);
        Assert.Single((await ApiTestClient.Json(logsAfter))!["items"]!.AsArray());
        using var jobsAfter = await client.GetAsync("/api/v1/processing/jobs/inspection?status=failed");
        Assert.Single((await ApiTestClient.Json(jobsAfter))!["jobs"]!.AsArray());

        // The job row, and its dedupe key, are untouched: a resend that lands on the very same key is still
        // refused rather than starting a second pass.
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM jobs WHERE dedupe_key = $dedupe", ("$dedupe", dedupeKey)));
    }

    [Fact]
    public async Task A_file_skipped_under_the_minimum_size_says_so_with_a_code_and_any_other_skip_has_none()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var libraryId = await SeedLibraryAsync(server);
        await SeedFileAsync(server, libraryId, "Film/Gallery.mkv", "skipped");
        await SeedFileAsync(server, libraryId, "Film/Huge.mkv", "skipped");
        await TestDatabase.ExecuteAsync(server, "UPDATE files SET skip_kind = 'below_minimum_size' WHERE relative_path = 'Film/Gallery.mkv'");

        using var response = await client.GetAsync("/api/v1/processing/files?file_status=skipped");

        var kinds = (await ApiTestClient.Json(response))!["files"]!.AsArray()
            .ToDictionary(file => file!["relative_path"]!.GetValue<string>(), file => file!["skip_kind"]?.GetValue<string>());
        Assert.Equal("below_minimum_size", kinds["Film/Gallery.mkv"]);
        Assert.Null(kinds["Film/Huge.mkv"]);
    }

    private static async Task RecordPassAsync(WeirTestServer server, long libraryId, string relativePath, string title, bool ok)
    {
        var writer = server.Services.GetRequiredService<IActivityWriter>();
        await writer.RecordAsync(new ActivityEventDraft(
            ActivityEventTypes.ProcessingFileRemuxPassCompleted,
            "processing",
            title,
            $"{{\"library_id\": {libraryId}, \"relative_media_path\": \"{relativePath}\", \"ok\": {(ok ? "true" : "false")}, \"outcome\": \"{(ok ? "live_output_written" : "failed_during_execution")}\"}}"));
    }

    /// <summary>A pass that finished nothing: its file was skipped, as the pass records it (result skipped, ok true).</summary>
    private static async Task RecordSkippedPassAsync(WeirTestServer server, long libraryId, string relativePath, string title)
    {
        var writer = server.Services.GetRequiredService<IActivityWriter>();
        await writer.RecordAsync(new ActivityEventDraft(
            ActivityEventTypes.ProcessingFileRemuxPassCompleted,
            "processing",
            title,
            $"{{\"library_id\": {libraryId}, \"relative_media_path\": \"{relativePath}\", \"ok\": true, \"outcome\": \"skipped_guardrail\", \"result\": \"skipped\"}}"));
    }

    [Fact]
    public async Task A_later_skip_does_not_replace_the_pass_that_cleaned_the_file()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var libraryId = await SeedLibraryAsync(server);
        await SeedFileAsync(server, libraryId, "Film/film.mkv", "processed");
        await RecordPassAsync(server, libraryId, "Film/film.mkv", "Film cleaned", ok: true);
        await RecordSkippedPassAsync(server, libraryId, "Film/film.mkv", "Film skipped");

        Assert.Equal(["Film cleaned"], await PassTitlesAsync(client, "&current_only=true"));
        Assert.Equal(2, (await PassTitlesAsync(client, string.Empty)).Length);
    }

    [Fact]
    public async Task A_film_with_many_skipped_extras_still_fills_the_page_with_the_film_and_counts_only_it()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var libraryId = await SeedLibraryAsync(server);
        await SeedFileAsync(server, libraryId, "Film/film.mkv", "processed");
        await RecordPassAsync(server, libraryId, "Film/film.mkv", "Film cleaned", ok: true);
        foreach (var extra in Enumerable.Range(1, 6))
        {
            await RecordSkippedPassAsync(server, libraryId, $"Film/extra{extra}.mkv", $"Extra {extra} skipped");
        }

        Assert.Equal(["Film cleaned"], await PassTitlesAsync(client, "&current_only=true&limit=3"));
        using var response = await client.GetAsync("/api/v1/activity/recent?event_type=" + ActivityEventTypes.ProcessingFileRemuxPassCompleted + "&current_only=true");
        Assert.Equal(1, (await ApiTestClient.Json(response))!["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_list_that_shows_no_count_can_ask_for_none()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();

        using var counted = await client.GetAsync("/api/v1/activity/recent");
        using var uncounted = await client.GetAsync("/api/v1/activity/recent?with_total=false");

        Assert.NotNull((await ApiTestClient.Json(counted))!["total"]);
        Assert.Null((await ApiTestClient.Json(uncounted))!["total"]);
    }

    private static async Task<string[]> PassTitlesAsync(ApiTestClient client, string query)
    {
        using var response = await client.GetAsync("/api/v1/activity/recent?event_type=" + ActivityEventTypes.ProcessingFileRemuxPassCompleted + query);
        return [.. (await ApiTestClient.Json(response))!["items"]!.AsArray().Select(item => item!["title"]!.GetValue<string>())];
    }

    /// <summary>
    /// A list of what finished asks for <c>current_only</c>, and sees each file as it stands now: a pass that a later pass of the
    /// same file replaces, and a failure of a file that has since been processed, are left out, and a file that is still failed
    /// keeps its failure. System's log never asks, and keeps every entry.
    /// </summary>
    [Fact]
    public async Task The_current_list_shows_each_file_as_it_stands_now_and_the_log_keeps_every_entry()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var libraryId = await SeedLibraryAsync(server);
        await SeedFileAsync(server, libraryId, "Retried/retried.mkv", "processed");
        await SeedFileAsync(server, libraryId, "Settled/settled.mkv", "processed");
        await SeedFileAsync(server, libraryId, "Broken/broken.mkv", "processing_failed");
        await SeedFileAsync(server, libraryId, "Again/again.mkv", "processed");
        await RecordPassAsync(server, libraryId, "Again/again.mkv", "Again first pass", ok: true);
        await RecordPassAsync(server, libraryId, "Retried/retried.mkv", "Retried first try", ok: false);
        await RecordPassAsync(server, libraryId, "Retried/retried.mkv", "Retried second try", ok: true);
        await RecordPassAsync(server, libraryId, "Settled/settled.mkv", "Settled delivered", ok: true);
        await RecordPassAsync(server, libraryId, "Settled/settled.mkv", "Settled reported failed", ok: false);
        await RecordPassAsync(server, libraryId, "Broken/broken.mkv", "Broken failed", ok: false);

        await RecordPassAsync(server, libraryId, "Again/again.mkv", "Again second pass", ok: true);

        Assert.Equal(
            ["Again second pass", "Broken failed", "Retried second try", "Settled delivered"],
            (await PassTitlesAsync(client, "&current_only=true")).Order(StringComparer.Ordinal));
        Assert.Equal(7, (await PassTitlesAsync(client, string.Empty)).Length);
    }

    /// <summary>
    /// The list follows the file: once the file is processed, its recorded failure is no longer the answer, with nothing deleted.
    /// </summary>
    [Fact]
    public async Task A_failure_stops_being_listed_once_the_file_is_processed_and_the_entry_is_still_in_the_log()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var libraryId = await SeedLibraryAsync(server);
        await SeedFileAsync(server, libraryId, "Film/film.mkv", "processing_failed");
        await RecordPassAsync(server, libraryId, "Film/film.mkv", "Film could not be processed", ok: false);
        Assert.Equal(["Film could not be processed"], await PassTitlesAsync(client, "&current_only=true"));

        await TestDatabase.ExecuteAsync(server, "UPDATE files SET status = 'processed' WHERE relative_path = 'Film/film.mkv'");

        Assert.Empty(await PassTitlesAsync(client, "&current_only=true"));
        Assert.Equal(["Film could not be processed"], await PassTitlesAsync(client, string.Empty));
    }

    /// <summary>
    /// The overview's processed/failed counts, output-written total and space saved are lifetime statistics —
    /// "Weir has saved X GB" — the same kind of fact System › Logs keeps whether or not a title is still in
    /// Activity. Removing a title from Activity must not change them, even though the same title drops out of the
    /// failed-jobs alert and the "Just finished" lane in the process.
    /// </summary>
    [Fact]
    public async Task Removing_a_file_through_historys_own_endpoint_leaves_the_overviews_lifetime_totals_unchanged()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var libraryId = await SeedLibraryAsync(server);

        var finishedId = await SeedFileWithIdAsync(server, libraryId, "Heat/heat.mkv", "processed");
        var failedId = await SeedFileWithIdAsync(server, libraryId, "Up/up.mkv", "processing_failed");

        var writer = server.Services.GetRequiredService<IActivityWriter>();
        await writer.RecordAsync(new ActivityEventDraft(
            ActivityEventTypes.ProcessingFileRemuxPassCompleted,
            "processing",
            "Heat finished",
            $"{{\"library_id\": {libraryId}, \"relative_media_path\": \"Heat/heat.mkv\", \"outcome\": \"live_output_written\", " +
            "\"source_size_bytes\": 1000, \"output_size_bytes\": 700}"));
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status) VALUES ('up-failed', 'processing.file.remux_pass.v1', $payload, 'failed')",
            ("$payload", $"{{\"relative_media_path\": \"Up/up.mkv\", \"library_id\": {libraryId}}}"));

        using var before = await client.GetAsync("/api/v1/processing/overview-stats");
        var beforeBody = await ApiTestClient.Json(before);

        foreach (var id in new[] { finishedId, failedId })
        {
            using var removed = await client.SendAsync(HttpMethod.Delete, $"/api/v1/processing/files/{id}", new { csrf_token = await client.CsrfAsync() });
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

        using var after = await client.GetAsync("/api/v1/processing/overview-stats");
        var afterBody = await ApiTestClient.Json(after);

        Assert.Equal(1, beforeBody!["files_processed"]!.GetValue<long>());
        Assert.Equal(1, beforeBody["output_written_count"]!.GetValue<long>());
        Assert.Equal(1, beforeBody["files_failed"]!.GetValue<long>());
        Assert.Equal(300, beforeBody["net_space_saved_bytes"]!.GetValue<long>());
        Assert.Equal(afterBody!["files_processed"]!.GetValue<long>(), beforeBody["files_processed"]!.GetValue<long>());
        Assert.Equal(afterBody["output_written_count"]!.GetValue<long>(), beforeBody["output_written_count"]!.GetValue<long>());
        Assert.Equal(afterBody["files_failed"]!.GetValue<long>(), beforeBody["files_failed"]!.GetValue<long>());
        Assert.Equal(afterBody["net_space_saved_bytes"]!.GetValue<long>(), beforeBody["net_space_saved_bytes"]!.GetValue<long>());

        // Meanwhile the same removal did clear the title from the live alert, proving the two are decided
        // independently rather than one flag governing both.
        using var jobsAlert = await client.GetAsync("/api/v1/processing/jobs/inspection?status=failed&known_files_only=true");
        Assert.Empty((await ApiTestClient.Json(jobsAlert))!["jobs"]!.AsArray());
    }
}

/// <summary>
/// The remove dialog's server side (#785): what <c>GET .../remove-options</c> reports before the dialog is shown,
/// and what each <c>resolution</c> on <c>DELETE .../files/{id}</c> actually does. The manager conversations
/// themselves are covered against real fakes in <c>HistoryFileRemovalServiceTests</c>; these are the HTTP contract
/// and the plain-remove fallback for a title that never qualifies for a choice.
/// </summary>
public sealed class ProcessingFilesRemoveDialogApiTests
{
    private static async Task<(long LibraryId, string Watched)> SeedLibraryWithFoldersAsync(WeirTestServer server)
    {
        var watched = Path.Combine(server.Home, "watch-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(server.Home, "out-" + Guid.NewGuid().ToString("N"));
        var work = Path.Combine(server.Home, "work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(watched);
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(work);
        var libraryId = await TestDatabase.ScalarAsync(
            server,
            "INSERT INTO libraries (name, media_type, watched_folder, output_folder, work_folder, display_order) " +
            "VALUES ($name, 'movie', $w, $o, $k, 1) RETURNING id",
            ("$name", "Movies " + Guid.NewGuid().ToString("N")[..8]), ("$w", watched), ("$o", output), ("$k", work));
        return (libraryId, watched);
    }

    /// <summary>
    /// A failed row carrying the fingerprint of the file actually on disk at <paramref name="fullPath"/>, exactly as
    /// it stands the moment a title becomes failed (#786 review of #785): without it, the remove dialog's identity
    /// check would refuse every seeded test file as "changed".
    /// </summary>
    private static async Task<long> SeedFailedFileAsync(WeirTestServer server, long libraryId, string relativePath, string fullPath)
    {
        var fingerprint = SourceFiles.Fingerprint(fullPath);
        return await TestDatabase.ScalarAsync(
            server,
            "INSERT INTO files (library_id, relative_path, status, status_reason, size_bytes, last_seen_at, fingerprint_size_bytes, fingerprint_mtime_ns) " +
            "VALUES ($lib, $path, 'processing_failed', 'Weir could not read the audio track.', $size, CURRENT_TIMESTAMP, $fsize, $fmtime) RETURNING id",
            ("$lib", libraryId), ("$path", relativePath), ("$size", fingerprint.SizeBytes), ("$fsize", fingerprint.SizeBytes), ("$fmtime", fingerprint.ModifiedTimeNs));
    }

    /// <summary>
    /// A failed row exactly as every title left by a pre-#786-follow-up Weir stands: nothing recorded in
    /// <c>fingerprint_size_bytes</c>/<c>fingerprint_mtime_ns</c>, since migration 0025 only fills those columns going
    /// forward. The remove dialog must offer the file's current details to confirm rather than refuse outright.
    /// </summary>
    private static async Task<long> SeedFailedFileWithoutFingerprintAsync(WeirTestServer server, long libraryId, string relativePath, long sizeBytes) =>
        await TestDatabase.ScalarAsync(
            server,
            "INSERT INTO files (library_id, relative_path, status, status_reason, size_bytes, last_seen_at) " +
            "VALUES ($lib, $path, 'processing_failed', 'Weir could not read the audio track.', $size, CURRENT_TIMESTAMP) RETURNING id",
            ("$lib", libraryId), ("$path", relativePath), ("$size", sizeBytes));

    [Fact]
    public async Task A_failed_title_whose_file_still_exists_qualifies_for_a_choice_with_no_manager_to_ask()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var (libraryId, watched) = await SeedLibraryWithFoldersAsync(server);
        var path = Path.Combine(watched, "film.mkv");
        await File.WriteAllBytesAsync(path, "not a real film"u8.ToArray());
        var fileId = await SeedFailedFileAsync(server, libraryId, "film.mkv", path);

        using var response = await client.GetAsync($"/api/v1/processing/files/{fileId}/remove-options");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiTestClient.Json(response);
        Assert.True(body!["requires_choice"]!.GetValue<bool>());
        Assert.False(body["delete_handled_by_manager"]!.GetValue<bool>());
        Assert.False(body["keep_notifies_manager"]!.GetValue<bool>());
        Assert.Null(body["manager_label"]);
    }

    [Fact]
    public async Task A_finished_title_never_requires_a_choice()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var (libraryId, watched) = await SeedLibraryWithFoldersAsync(server);
        await File.WriteAllBytesAsync(Path.Combine(watched, "film.mkv"), "done"u8.ToArray());
        var fileId = await TestDatabase.ScalarAsync(
            server,
            "INSERT INTO files (library_id, relative_path, status, last_seen_at) VALUES ($lib, 'film.mkv', 'processed', CURRENT_TIMESTAMP) RETURNING id",
            ("$lib", libraryId));

        using var response = await client.GetAsync($"/api/v1/processing/files/{fileId}/remove-options");

        Assert.False((await ApiTestClient.Json(response))!["requires_choice"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Resolution_delete_deletes_the_file_and_removes_it_from_history()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var (libraryId, watched) = await SeedLibraryWithFoldersAsync(server);
        var path = Path.Combine(watched, "film.mkv");
        await File.WriteAllBytesAsync(path, "not a real film"u8.ToArray());
        var fileId = await SeedFailedFileAsync(server, libraryId, "film.mkv", path);

        using var response = await client.SendAsync(
            HttpMethod.Delete, $"/api/v1/processing/files/{fileId}", new { csrf_token = await client.CsrfAsync(), resolution = "delete" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiTestClient.Json(response);
        Assert.True(body!["done"]!.GetValue<bool>());
        Assert.False(File.Exists(path));
        Assert.Equal(0, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM files"));
    }

    [Fact]
    public async Task Resolution_keep_records_a_skip_marker_leaves_the_file_and_removes_it_from_history()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var (libraryId, watched) = await SeedLibraryWithFoldersAsync(server);
        var path = Path.Combine(watched, "film.mkv");
        await File.WriteAllBytesAsync(path, "not a real film"u8.ToArray());
        var fileId = await SeedFailedFileAsync(server, libraryId, "film.mkv", path);

        using var response = await client.SendAsync(
            HttpMethod.Delete, $"/api/v1/processing/files/{fileId}", new { csrf_token = await client.CsrfAsync(), resolution = "keep" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await ApiTestClient.Json(response))!["done"]!.GetValue<bool>());
        Assert.True(File.Exists(path));
        Assert.Equal(0, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM files"));
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM file_skip_markers"));
    }

    [Fact]
    public async Task Resolution_retry_queues_the_file_again_without_forgetting_it()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var (libraryId, watched) = await SeedLibraryWithFoldersAsync(server);
        var path = Path.Combine(watched, "film.mkv");
        await File.WriteAllBytesAsync(path, "not a real film"u8.ToArray());
        var fileId = await SeedFailedFileAsync(server, libraryId, "film.mkv", path);

        using var response = await client.SendAsync(
            HttpMethod.Delete, $"/api/v1/processing/files/{fileId}", new { csrf_token = await client.CsrfAsync(), resolution = "retry" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiTestClient.Json(response);
        Assert.True(body!["done"]!.GetValue<bool>());
        Assert.Contains("Queued again", body["detail"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.True(File.Exists(path));
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM files"));
        Assert.Equal("unprocessed", await TestDatabase.ScalarStringAsync(server, "SELECT status FROM files WHERE id = $id", ("$id", fileId)));
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM jobs WHERE job_kind = 'processing.file.remux_pass.v1'"));
    }

    [Fact]
    public async Task Resolution_retry_reports_a_skip_reason_instead_of_claiming_it_queued()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var (libraryId, watched) = await SeedLibraryWithFoldersAsync(server);
        var path = Path.Combine(watched, "film.mkv");
        await File.WriteAllBytesAsync(path, "not a real film"u8.ToArray());
        var fileId = await SeedFailedFileAsync(server, libraryId, "film.mkv", path);
        // A concluded file with its original gone: RequeueFileAsync skips rather than queues it (#786 review of #785).
        await TestDatabase.ExecuteAsync(server, "UPDATE files SET status = 'rejected' WHERE id = $id", ("$id", fileId));
        File.Delete(path);

        using var response = await client.SendAsync(
            HttpMethod.Delete, $"/api/v1/processing/files/{fileId}", new { csrf_token = await client.CsrfAsync(), resolution = "retry" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiTestClient.Json(response);
        Assert.False(body!["done"]!.GetValue<bool>());
        Assert.Contains("no longer in the watched folder", body["detail"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(0, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM jobs WHERE job_kind = 'processing.file.remux_pass.v1'"));
    }

    [Fact]
    public async Task An_unknown_resolution_is_rejected_with_422()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var (libraryId, watched) = await SeedLibraryWithFoldersAsync(server);
        var path = Path.Combine(watched, "film.mkv");
        await File.WriteAllBytesAsync(path, "not a real film"u8.ToArray());
        var fileId = await SeedFailedFileAsync(server, libraryId, "film.mkv", path);

        using var response = await client.SendAsync(
            HttpMethod.Delete, $"/api/v1/processing/files/{fileId}", new { csrf_token = await client.CsrfAsync(), resolution = "discard" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM files"));
    }

    [Fact]
    public async Task A_viewer_cannot_read_remove_options()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedViewerAsync(server);
        var (libraryId, watched) = await SeedLibraryWithFoldersAsync(server);
        var path = Path.Combine(watched, "film.mkv");
        await File.WriteAllBytesAsync(path, "not a real film"u8.ToArray());
        var fileId = await SeedFailedFileAsync(server, libraryId, "film.mkv", path);
        var viewer = new ApiTestClient(server);
        await viewer.SignInAsync("bob", ApiTestClient.ViewerPassword);

        using var response = await viewer.GetAsync($"/api/v1/processing/files/{fileId}/remove-options");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// A pre-#786-follow-up row (#786 follow-up): every title that failed or was rejected before migration 0025
    /// shipped has no recorded fingerprint. Refusing outright would strand exactly the titles an owner is trying
    /// to clear right after upgrading, so remove-options instead offers the file's current details to confirm.
    /// </summary>
    [Fact]
    public async Task A_row_with_no_recorded_fingerprint_offers_the_files_current_details_over_http()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var (libraryId, watched) = await SeedLibraryWithFoldersAsync(server);
        var path = Path.Combine(watched, "film.mkv");
        await File.WriteAllBytesAsync(path, "not a real film"u8.ToArray());
        var fileId = await SeedFailedFileWithoutFingerprintAsync(server, libraryId, "film.mkv", new FileInfo(path).Length);

        using var response = await client.GetAsync($"/api/v1/processing/files/{fileId}/remove-options");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiTestClient.Json(response);
        Assert.True(body!["requires_choice"]!.GetValue<bool>());
        Assert.False(body["fingerprint_recorded"]!.GetValue<bool>());
        Assert.Equal(new FileInfo(path).Length, body["unconfirmed_size_bytes"]!.GetValue<long>());
        Assert.False(string.IsNullOrEmpty(body["unconfirmed_modified_at"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Resolution_delete_succeeds_for_a_pre_upgrade_row_when_the_confirmed_details_match()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var (libraryId, watched) = await SeedLibraryWithFoldersAsync(server);
        var path = Path.Combine(watched, "film.mkv");
        await File.WriteAllBytesAsync(path, "not a real film"u8.ToArray());
        var fileId = await SeedFailedFileWithoutFingerprintAsync(server, libraryId, "film.mkv", new FileInfo(path).Length);
        var options = await ApiTestClient.Json(await client.GetAsync($"/api/v1/processing/files/{fileId}/remove-options"));

        using var response = await client.SendAsync(
            HttpMethod.Delete,
            $"/api/v1/processing/files/{fileId}",
            new
            {
                csrf_token = await client.CsrfAsync(),
                resolution = "delete",
                confirm_size_bytes = options!["unconfirmed_size_bytes"]!.GetValue<long>(),
                confirm_modified_at = options["unconfirmed_modified_at"]!.GetValue<string>(),
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await ApiTestClient.Json(response))!["done"]!.GetValue<bool>());
        Assert.False(File.Exists(path));
        Assert.Equal(0, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM files"));
    }

    [Fact]
    public async Task Resolution_delete_refuses_a_pre_upgrade_row_when_the_confirmed_details_no_longer_match()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var (libraryId, watched) = await SeedLibraryWithFoldersAsync(server);
        var path = Path.Combine(watched, "film.mkv");
        await File.WriteAllBytesAsync(path, "not a real film"u8.ToArray());
        var fileId = await SeedFailedFileWithoutFingerprintAsync(server, libraryId, "film.mkv", new FileInfo(path).Length);
        var options = await ApiTestClient.Json(await client.GetAsync($"/api/v1/processing/files/{fileId}/remove-options"));
        // A different release lands at the same path between the dialog opening and the owner confirming.
        await File.WriteAllBytesAsync(path, "a completely different release replaced this one"u8.ToArray());

        using var response = await client.SendAsync(
            HttpMethod.Delete,
            $"/api/v1/processing/files/{fileId}",
            new
            {
                csrf_token = await client.CsrfAsync(),
                resolution = "delete",
                confirm_size_bytes = options!["unconfirmed_size_bytes"]!.GetValue<long>(),
                confirm_modified_at = options["unconfirmed_modified_at"]!.GetValue<string>(),
            });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("This file changed after you opened this dialog.", await ApiTestClient.Detail(response), StringComparison.Ordinal);
        Assert.True(File.Exists(path));
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM files"));
    }

    [Fact]
    public async Task Resolution_delete_refuses_a_pre_upgrade_row_with_no_confirmation_at_all()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var (libraryId, watched) = await SeedLibraryWithFoldersAsync(server);
        var path = Path.Combine(watched, "film.mkv");
        await File.WriteAllBytesAsync(path, "not a real film"u8.ToArray());
        var fileId = await SeedFailedFileWithoutFingerprintAsync(server, libraryId, "film.mkv", new FileInfo(path).Length);

        using var response = await client.SendAsync(
            HttpMethod.Delete, $"/api/v1/processing/files/{fileId}", new { csrf_token = await client.CsrfAsync(), resolution = "delete" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.True(File.Exists(path));
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM files"));
    }
}
