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
    /// #780: once an owner removes a finished or failed title from History (the <c>DELETE /processing/files/{id}</c>
    /// "Remove from list" action), Processing must stop reporting it too — its failed-jobs alert, its "Just finished"
    /// list, and the overview counters behind them, not only the Files list the removal itself reads from.
    /// </summary>
    [Fact]
    public async Task Removing_a_file_through_historys_own_endpoint_clears_it_from_every_processing_report()
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
        await writer.RecordAsync(new ActivityEventDraft(
            ActivityEventTypes.ProcessingFileRemuxPassCompleted,
            "processing",
            "Up failed",
            $"{{\"library_id\": {libraryId}, \"relative_media_path\": \"Up/up.mkv\", \"ok\": false, \"outcome\": \"failed_during_execution\"}}"));
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status) VALUES ('up-failed', 'processing.file.remux_pass.v1', $payload, 'failed')",
            ("$payload", $"{{\"relative_media_path\": \"Up/up.mkv\", \"library_id\": {libraryId}}}"));

        // Before removal: Processing's own endpoints report both the failed job and the two finished events.
        using var beforeJobs = await client.GetAsync("/api/v1/processing/jobs/inspection?status=failed");
        Assert.Single((await ApiTestClient.Json(beforeJobs))!["jobs"]!.AsArray());
        using var beforeFinished = await client.GetAsync("/api/v1/activity/recent?event_type=" + ActivityEventTypes.ProcessingFileRemuxPassCompleted);
        Assert.Equal(2, (await ApiTestClient.Json(beforeFinished))!["items"]!.AsArray().Count);
        using var beforeStats = await client.GetAsync("/api/v1/processing/overview-stats");
        Assert.Equal(1, (await ApiTestClient.Json(beforeStats))!["files_failed"]!.GetValue<long>());

        foreach (var id in new[] { finishedId, failedId })
        {
            using var removed = await client.SendAsync(HttpMethod.Delete, $"/api/v1/processing/files/{id}", new { csrf_token = await client.CsrfAsync() });
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

        using var afterJobs = await client.GetAsync("/api/v1/processing/jobs/inspection?status=failed");
        Assert.Empty((await ApiTestClient.Json(afterJobs))!["jobs"]!.AsArray());
        using var afterFinished = await client.GetAsync("/api/v1/activity/recent?event_type=" + ActivityEventTypes.ProcessingFileRemuxPassCompleted);
        Assert.Empty((await ApiTestClient.Json(afterFinished))!["items"]!.AsArray());
        using var afterStats = await client.GetAsync("/api/v1/processing/overview-stats");
        Assert.Equal(0, (await ApiTestClient.Json(afterStats))!["files_failed"]!.GetValue<long>());
        Assert.Equal(0, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM jobs WHERE dedupe_key = 'up-failed'"));
    }
}
