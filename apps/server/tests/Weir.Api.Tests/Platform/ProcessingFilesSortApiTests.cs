using System.Net;
using System.Text.Json.Nodes;

namespace Weir.Api.Tests.Platform;

/// <summary>The Files list over HTTP in each order it can be asked for, paged by cursor.</summary>
public sealed class ProcessingFilesSortApiTests
{
    private const string Files = "/api/v1/processing/files";

    private static async Task<(WeirTestServer Server, ApiTestClient Client)> StartWithFilesAsync()
    {
        var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var library = await TestDatabase.ScalarAsync(server, "INSERT INTO libraries (name, media_type) VALUES ('Files sorted', 'movie') RETURNING id");
        foreach (var (path, status, updatedAt) in new[]
        {
            ("b.mkv", "processed", "2026-10-02 10:00:00"),
            ("A.mkv", "processing_failed", "2026-10-03 10:00:00"),
            ("c.mkv", "unprocessed", "2026-10-01 10:00:00"),
        })
        {
            await TestDatabase.ExecuteAsync(
                server,
                "INSERT INTO files (library_id, relative_path, status, last_seen_at, updated_at) VALUES ($lib, $path, $status, '2026-10-02 10:00:00', $updated)",
                ("$lib", library), ("$path", path), ("$status", status), ("$updated", updatedAt));
        }

        return (server, client);
    }

    private static IEnumerable<string> Paths(JsonNode page) => page["files"]!.AsArray().Select(file => file!["relative_path"]!.GetValue<string>());

    [Theory]
    [InlineData("sort=file&direction=asc", new[] { "A.mkv", "b.mkv", "c.mkv" })]
    [InlineData("sort=file&direction=desc", new[] { "c.mkv", "b.mkv", "A.mkv" })]
    [InlineData("sort=status&direction=asc", new[] { "b.mkv", "c.mkv", "A.mkv" })]
    [InlineData("sort=status&direction=desc", new[] { "A.mkv", "c.mkv", "b.mkv" })]
    [InlineData("sort=when", new[] { "A.mkv", "b.mkv", "c.mkv" })]
    [InlineData("sort=when&direction=asc", new[] { "c.mkv", "b.mkv", "A.mkv" })]
    public async Task The_list_comes_in_the_order_asked_for(string query, string[] expected)
    {
        var (server, client) = await StartWithFilesAsync();
        await using var _ = server;

        var page = await ApiTestClient.Json(await client.GetAsync($"{Files}?{query}"));

        Assert.Equal(expected, Paths(page));
        Assert.Null(page["next_cursor"]);
    }

    [Fact]
    public async Task A_cursor_continues_the_list_in_the_same_order_until_the_last_file()
    {
        var (server, client) = await StartWithFilesAsync();
        await using var _ = server;

        var first = await ApiTestClient.Json(await client.GetAsync($"{Files}?sort=file&direction=asc&limit=2"));
        var cursor = Uri.EscapeDataString(first["next_cursor"]!.GetValue<string>());
        var second = await ApiTestClient.Json(await client.GetAsync($"{Files}?sort=file&direction=asc&limit=2&cursor={cursor}"));

        Assert.Equal(["A.mkv", "b.mkv"], Paths(first));
        Assert.Equal(["c.mkv"], Paths(second));
        Assert.Null(second["next_cursor"]);
        Assert.Equal(1, second["returned"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("sort=name")]
    [InlineData("sort=")]
    [InlineData("sort=last_seen")]
    [InlineData("direction=up")]
    [InlineData("sort=file&direction=")]
    [InlineData("sort=file&cursor=nonsense")]
    public async Task A_sort_direction_or_cursor_the_list_does_not_understand_is_refused(string query)
    {
        var (server, client) = await StartWithFilesAsync();
        await using var _ = server;

        using var response = await client.GetAsync($"{Files}?{query}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.NotEmpty((await ApiTestClient.Json(response))["detail"]!.AsArray());
    }

    [Theory]
    [InlineData("sort=file&direction=desc")]
    [InlineData("sort=status&direction=asc")]
    [InlineData("sort=when&direction=asc")]
    [InlineData("direction=asc")]
    public async Task A_cursor_made_for_another_sort_or_direction_is_refused(string otherOrder)
    {
        var (server, client) = await StartWithFilesAsync();
        await using var _ = server;
        var first = await ApiTestClient.Json(await client.GetAsync($"{Files}?sort=file&direction=asc&limit=1"));

        using var response = await client.GetAsync($"{Files}?{otherOrder}&cursor={Uri.EscapeDataString(first["next_cursor"]!.GetValue<string>())}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task A_cleaned_copy_waiting_for_its_media_manager_sorts_with_the_files_in_progress_and_still_reads_processed()
    {
        var (server, client) = await StartWithFilesAsync();
        await using var _ = server;
        var library = await TestDatabase.ScalarAsync(server, "SELECT id FROM libraries WHERE name = 'Files sorted'");
        await TestDatabase.ExecuteAsync(server, "INSERT INTO media_manager_connections (kind, name, base_url) VALUES ('radarr', 'Radarr', 'http://192.0.2.20:7878')");
        await TestDatabase.ExecuteAsync(server, "INSERT INTO library_manager_links (library_id, connection_id) SELECT $lib, id FROM media_manager_connections", ("$lib", library));
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO files (library_id, relative_path, status, last_seen_at, updated_at) VALUES ($lib, 'd.mkv', 'processed', '2026-10-02 10:00:00', '2026-10-02 10:00:00')",
            ("$lib", library));
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO handbacks (library_id, relative_path, output_path, output_size, output_mtime_ns, written_at) VALUES ($lib, 'b.mkv', '/out/b.mkv', 1, 1, '2026-10-02 09:00:00')",
            ("$lib", library));

        var page = await ApiTestClient.Json(await client.GetAsync($"{Files}?sort=status&direction=asc"));

        Assert.Equal(["d.mkv", "b.mkv", "c.mkv", "A.mkv"], Paths(page));
        var waiting = page["files"]![1]!;
        Assert.Equal("processed", waiting["status"]!.GetValue<string>());
        Assert.Null(waiting["handback"]!["outcome"]);
    }
}
