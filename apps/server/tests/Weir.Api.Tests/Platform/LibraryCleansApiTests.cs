using System.Net;
using System.Text.Json.Nodes;

namespace Weir.Api.Tests.Platform;

/// <summary>
/// <c>GET /processing/library-cleans</c>: History's library cleans, one entry per file with its newest outcome, each
/// naming its kind, filtered the way the download list is (#695).
/// </summary>
public sealed class LibraryCleansApiTests
{
    private static async Task<(ApiTestClient Client, long LibraryId)> SignInWithALibraryAsync(WeirTestServer server)
    {
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var libraryId = await TestDatabase.ScalarAsync(
            server,
            "INSERT INTO libraries (name, media_type) VALUES ('Films', 'movie') RETURNING id");
        return (client, libraryId);
    }

    private static Task SeedEventAsync(WeirTestServer server, string eventType, long libraryId, string path, string title, string createdAt) =>
        TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO activity_events (created_at, event_type, module, title, library_id, relative_path) " +
            "VALUES ($at, $type, 'library', $title, $lib, $path)",
            ("$at", createdAt), ("$type", eventType), ("$title", title), ("$lib", libraryId), ("$path", path));

    private static async Task<JsonArray> CleansAsync(ApiTestClient client, string query = "")
    {
        using var response = await client.GetAsync("/api/v1/processing/library-cleans" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ApiTestClient.Json(response))["cleans"]!.AsArray();
    }

    [Fact]
    public async Task A_file_cleaned_after_a_failed_attempt_is_listed_once_as_cleaned()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        var (client, libraryId) = await SignInWithALibraryAsync(server);
        await SeedEventAsync(server, "library.file_failed", libraryId, "/films/Heat (1995).mkv", "The file was in use.", "2026-09-20 10:00:00");
        await SeedEventAsync(server, "library.file_cleaned", libraryId, "/films/Heat (1995).mkv", "Cleaned Heat (1995).mkv: removed 1 audio track.", "2026-09-21 10:00:00");

        var clean = Assert.Single(await CleansAsync(client));

        Assert.Equal("library_clean", clean!["kind"]!.GetValue<string>());
        Assert.Equal("cleaned", clean["outcome"]!.GetValue<string>());
        Assert.Equal("Cleaned Heat (1995).mkv: removed 1 audio track.", clean["detail"]!.GetValue<string>());
        Assert.Equal("Films", clean["library_name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Events_that_are_not_about_a_library_clean_are_left_out()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        var (client, libraryId) = await SignInWithALibraryAsync(server);
        await SeedEventAsync(server, "processing.file_remux_pass_completed", libraryId, "Heat/heat.mkv", "Finished.", "2026-09-21 10:00:00");
        await SeedEventAsync(server, "library.file_skipped", libraryId, "/films/Up.mkv", "This file already matches the library's rules; nothing to clean.", "2026-09-21 11:00:00");

        var clean = Assert.Single(await CleansAsync(client));

        Assert.Equal("skipped", clean!["outcome"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_path_filter_narrows_the_list_to_matching_files()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        var (client, libraryId) = await SignInWithALibraryAsync(server);
        await SeedEventAsync(server, "library.file_cleaned", libraryId, "/films/Heat (1995).mkv", "Cleaned.", "2026-09-21 10:00:00");
        await SeedEventAsync(server, "library.file_cleaned", libraryId, "/films/Up (2009).mkv", "Cleaned.", "2026-09-21 11:00:00");

        var clean = Assert.Single(await CleansAsync(client, "?path_contains=heat"));

        Assert.Equal("/films/Heat (1995).mkv", clean!["relative_path"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_clean_older_than_the_period_asked_for_is_left_out()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        var (client, libraryId) = await SignInWithALibraryAsync(server);
        await SeedEventAsync(server, "library.file_cleaned", libraryId, "/films/Old.mkv", "Cleaned.", "2001-01-01 10:00:00");

        Assert.Empty(await CleansAsync(client, "?within_days=7"));
    }

    [Fact]
    public async Task Every_download_in_the_file_list_names_its_kind()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        var (client, libraryId) = await SignInWithALibraryAsync(server);
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO files (library_id, relative_path, status, last_seen_at) VALUES ($lib, 'Heat/heat.mkv', 'processed', CURRENT_TIMESTAMP)",
            ("$lib", libraryId));

        using var response = await client.GetAsync("/api/v1/processing/files");

        var file = Assert.Single((await ApiTestClient.Json(response))["files"]!.AsArray());
        Assert.Equal("download", file!["kind"]!.GetValue<string>());
    }
}
