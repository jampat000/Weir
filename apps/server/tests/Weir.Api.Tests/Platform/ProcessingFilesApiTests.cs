using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Weir.Core.Activity;

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
    /// Once an owner removes a finished or failed title from History (the <c>DELETE /processing/files/{id}</c>
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

    /// <summary>
    /// The overview's processed/failed counts, output-written total and space saved are lifetime statistics —
    /// "Weir has saved X GB" — the same kind of fact System › Logs keeps whether or not a title is still in
    /// History. Removing a title from History must not change them, even though the same title drops out of the
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
