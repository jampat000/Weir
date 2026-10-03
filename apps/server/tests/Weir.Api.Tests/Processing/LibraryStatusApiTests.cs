using System.Net;
using System.Text.Json.Nodes;
using Weir.Api.Tests.Platform;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.Processing;

/// <summary>
/// Where each library file stands now, against the current rules: exactly one status per file, so the counts add up to the
/// files. Left alone beats Cleaning, which beats Can't clean yet, which beats Needs cleaning, which beats Matches. What Weir
/// did to a file once is history beside the status, never a status of its own.
/// </summary>
public sealed class LibraryStatusApiTests
{
    private static string FilesPath(long libraryId, string query = "") =>
        $"/api/v1/processing/libraries/{libraryId}/library-files" + (query.Length > 0 ? "?" + query : "");

    private static string OverviewPath(long libraryId) => $"/api/v1/processing/libraries/{libraryId}/library-overview";

    private static async Task<(WeirTestServer Server, ApiTestClient Client, long LibraryId)> StartAsync()
    {
        var server = await StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var libraryId = await TestDatabase.ScalarAsync(
            server,
            "INSERT INTO libraries (name, media_type, watched_folder, output_folder, work_folder, display_order) " +
            "VALUES ('Status', 'movie', '/in', '/out', '/work', 9) RETURNING id");
        return (server, client, libraryId);
    }

    private static Task SeedFileAsync(
        WeirTestServer server, long libraryId, string path, string classification, int? linkCount = null, string? problemKind = null, string? changeReason = null) =>
        TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO library_files (library_id, path, classification, link_count, problem_kind, change_reason) " +
            "VALUES ($library, $path, $classification, $links, $problem, $reason)",
            ("$library", libraryId),
            ("$path", path),
            ("$classification", classification),
            ("$links", (object?)linkCount ?? DBNull.Value),
            ("$problem", (object?)problemKind ?? DBNull.Value),
            ("$reason", (object?)changeReason ?? DBNull.Value));

    private static Task QueueCleanAsync(WeirTestServer server, long libraryId, string path, string status = "pending") =>
        TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status) VALUES ($key, 'processing.library.clean.v1', $payload, $status)",
            ("$key", $"processing.library.clean.v1:{libraryId}:{Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(path))}"),
            ("$payload", new JsonObject { ["library_id"] = libraryId, ["path"] = path }.ToJsonString()),
            ("$status", status));

    private static Task MarkAsync(WeirTestServer server, long libraryId, string path, string? cleanedAt = null, bool leaveAlone = false) =>
        TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO library_file_marks (library_id, path, cleaned_at, leave_alone) VALUES ($library, $path, $cleaned, $leave)",
            ("$library", libraryId),
            ("$path", path),
            ("$cleaned", (object?)cleanedAt ?? DBNull.Value),
            ("$leave", leaveAlone ? 1 : 0));

    private static async Task<Dictionary<string, JsonNode>> FilesAsync(ApiTestClient client, long libraryId, string query = "")
    {
        using var response = await client.GetAsync(FilesPath(libraryId, query));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response))["files"]!.AsArray().ToDictionary(file => file!["path"]!.GetValue<string>(), file => file!);
    }

    private static string StatusOf(Dictionary<string, JsonNode> files, string path) => files[path]["status"]!.GetValue<string>();

    [Fact]
    public async Task Every_file_has_one_status_and_the_counts_add_up_to_the_files()
    {
        var (server, client, libraryId) = await StartAsync();
        await using var _ = server;
        await SeedFileAsync(server, libraryId, "/lib/matches.mkv", "matches");
        await SeedFileAsync(server, libraryId, "/lib/needs.mkv", "would_change", changeReason: "new");
        await SeedFileAsync(server, libraryId, "/lib/unreadable.mkv", "cannot_process", problemKind: "unreadable");
        await SeedFileAsync(server, libraryId, "/lib/seeding.mkv", "would_change", linkCount: 2);
        await SeedFileAsync(server, libraryId, "/lib/cleaning.mkv", "would_change");
        await QueueCleanAsync(server, libraryId, "/lib/cleaning.mkv");
        await SeedFileAsync(server, libraryId, "/lib/aside.mkv", "would_change");
        await MarkAsync(server, libraryId, "/lib/aside.mkv", leaveAlone: true);

        var files = await FilesAsync(client, libraryId);

        Assert.Equal("matches", StatusOf(files, "/lib/matches.mkv"));
        Assert.Equal("needs_cleaning", StatusOf(files, "/lib/needs.mkv"));
        Assert.Equal("cant_clean_yet", StatusOf(files, "/lib/unreadable.mkv"));
        Assert.Equal("cant_clean_yet", StatusOf(files, "/lib/seeding.mkv"));
        Assert.Equal("cleaning", StatusOf(files, "/lib/cleaning.mkv"));
        Assert.Equal("left_alone", StatusOf(files, "/lib/aside.mkv"));

        using var overview = await client.GetAsync(OverviewPath(libraryId));
        var counts = (await Json(overview))["totals"]!["by_status"]!;
        Assert.Equal(1, counts["needs_cleaning"]!.GetValue<int>());
        Assert.Equal(1, counts["cleaning"]!.GetValue<int>());
        Assert.Equal(1, counts["matches"]!.GetValue<int>());
        Assert.Equal(2, counts["cant_clean_yet"]!.GetValue<int>());
        Assert.Equal(1, counts["left_alone"]!.GetValue<int>());
        Assert.Equal(6, counts.AsObject().Sum(count => count.Value!.GetValue<int>()));
    }

    [Fact]
    public async Task Left_alone_beats_cleaning_and_cleaning_beats_cant_clean_yet()
    {
        var (server, client, libraryId) = await StartAsync();
        await using var _ = server;
        await SeedFileAsync(server, libraryId, "/lib/aside.mkv", "would_change");
        await QueueCleanAsync(server, libraryId, "/lib/aside.mkv");
        await MarkAsync(server, libraryId, "/lib/aside.mkv", leaveAlone: true);
        await SeedFileAsync(server, libraryId, "/lib/seeding.mkv", "would_change", linkCount: 2);
        await QueueCleanAsync(server, libraryId, "/lib/seeding.mkv", status: "leased");

        var files = await FilesAsync(client, libraryId);

        Assert.Equal("left_alone", StatusOf(files, "/lib/aside.mkv"));
        Assert.Equal("cleaning", StatusOf(files, "/lib/seeding.mkv"));
    }

    [Fact]
    public async Task A_clean_that_has_finished_is_no_longer_cleaning()
    {
        var (server, client, libraryId) = await StartAsync();
        await using var _ = server;
        await SeedFileAsync(server, libraryId, "/lib/film.mkv", "would_change");
        await QueueCleanAsync(server, libraryId, "/lib/film.mkv", status: "completed");

        Assert.Equal("needs_cleaning", StatusOf(await FilesAsync(client, libraryId), "/lib/film.mkv"));
    }

    [Fact]
    public async Task A_clean_queued_in_another_library_does_not_make_this_one_cleaning()
    {
        var (server, client, libraryId) = await StartAsync();
        await using var _ = server;
        await SeedFileAsync(server, libraryId, "/lib/film.mkv", "would_change");
        await QueueCleanAsync(server, libraryId + 1, "/lib/film.mkv");

        Assert.Equal("needs_cleaning", StatusOf(await FilesAsync(client, libraryId), "/lib/film.mkv"));
    }

    [Fact]
    public async Task A_file_that_matches_and_was_once_cleaned_is_matching_with_its_history_beside_it()
    {
        var (server, client, libraryId) = await StartAsync();
        await using var _ = server;
        await SeedFileAsync(server, libraryId, "/lib/cleaned.mkv", "matches");
        await MarkAsync(server, libraryId, "/lib/cleaned.mkv", cleanedAt: "2026-10-03 09:00:00.000000");

        var files = await FilesAsync(client, libraryId);

        Assert.Equal("matches", StatusOf(files, "/lib/cleaned.mkv"));
        Assert.NotNull(files["/lib/cleaned.mkv"]["cleaned_at"]);
    }

    [Fact]
    public async Task A_shared_file_needs_cleaning_when_the_library_cleans_hardlinked_files()
    {
        var (server, client, libraryId) = await StartAsync();
        await using var _ = server;
        await SeedFileAsync(server, libraryId, "/lib/seeding.mkv", "would_change", linkCount: 2);
        Assert.Equal("cant_clean_yet", StatusOf(await FilesAsync(client, libraryId), "/lib/seeding.mkv"));

        using var saved = await client.PutAsync(
            $"/api/v1/processing/libraries/{libraryId}/library-settings",
            new { library_folders = Array.Empty<string>(), clean_hardlinked_files = true, csrf_token = await client.CsrfAsync() });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        Assert.Equal("needs_cleaning", StatusOf(await FilesAsync(client, libraryId), "/lib/seeding.mkv"));
    }

    [Fact]
    public async Task A_recorded_problem_on_a_file_the_rules_would_change_means_it_cannot_be_cleaned_yet()
    {
        var (server, client, libraryId) = await StartAsync();
        await using var _ = server;
        await SeedFileAsync(server, libraryId, "/lib/risky.mkv", "would_change", problemKind: "manager_redownload");

        Assert.Equal("cant_clean_yet", StatusOf(await FilesAsync(client, libraryId), "/lib/risky.mkv"));
    }

    [Fact]
    public async Task The_reason_a_file_needs_cleaning_is_given_only_for_a_file_that_does()
    {
        var (server, client, libraryId) = await StartAsync();
        await using var _ = server;
        await SeedFileAsync(server, libraryId, "/lib/new.mkv", "would_change", changeReason: "new");
        await SeedFileAsync(server, libraryId, "/lib/unknown.mkv", "would_change");
        await SeedFileAsync(server, libraryId, "/lib/aside.mkv", "would_change", changeReason: "replaced");
        await MarkAsync(server, libraryId, "/lib/aside.mkv", leaveAlone: true);

        var files = await FilesAsync(client, libraryId);

        Assert.Equal("new", files["/lib/new.mkv"]["status_reason"]!.GetValue<string>());
        Assert.Null(files["/lib/unknown.mkv"]["status_reason"]);
        Assert.Null(files["/lib/aside.mkv"]["status_reason"]);
    }

    [Fact]
    public async Task The_files_table_narrows_to_one_status_and_ignores_a_status_it_does_not_know()
    {
        var (server, client, libraryId) = await StartAsync();
        await using var _ = server;
        await SeedFileAsync(server, libraryId, "/lib/matches.mkv", "matches");
        await SeedFileAsync(server, libraryId, "/lib/needs.mkv", "would_change");
        await SeedFileAsync(server, libraryId, "/lib/cleaning.mkv", "would_change");
        await QueueCleanAsync(server, libraryId, "/lib/cleaning.mkv");

        Assert.Equal(["/lib/needs.mkv"], (await FilesAsync(client, libraryId, "status=needs_cleaning")).Keys);
        Assert.Equal(["/lib/cleaning.mkv"], (await FilesAsync(client, libraryId, "status=cleaning")).Keys);
        Assert.Equal(["/lib/matches.mkv"], (await FilesAsync(client, libraryId, "status=matches")).Keys);
        Assert.Empty(await FilesAsync(client, libraryId, "status=left_alone"));
        Assert.Equal(3, (await FilesAsync(client, libraryId, "status=nonsense")).Count);

        using var response = await client.GetAsync(FilesPath(libraryId, "status=needs_cleaning"));
        var body = await Json(response);
        Assert.Equal(1, body["total"]!.GetValue<int>());
        Assert.Equal(3, body["summary"]!["files"]!.GetValue<int>());
        Assert.Equal(1, body["filtered"]!["by_status"]!["needs_cleaning"]!.GetValue<int>());
        Assert.Equal(0, body["filtered"]!["by_status"]!["matches"]!.GetValue<int>());
    }
}
