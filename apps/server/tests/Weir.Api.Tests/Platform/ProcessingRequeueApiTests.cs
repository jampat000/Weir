using System.Net;
using System.Text.Json.Nodes;

namespace Weir.Api.Tests.Platform;

/// <summary>
/// Queueing files again from History: the exact files a person chose, and a finished file only while its original is
/// still in the watched folder.
/// </summary>
public sealed class ProcessingRequeueApiTests
{
    private static async Task<(ApiTestClient Client, long LibraryId, string WatchedFolder)> SignInWithALibraryAsync(WeirTestServer server)
    {
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var watched = Directory.CreateDirectory(Path.Join(server.Home, "watched")).FullName;
        var libraryId = await TestDatabase.ScalarAsync(
            server,
            "INSERT INTO libraries (name, media_type, watched_folder) VALUES ('Films', 'movie', $watched) RETURNING id",
            ("$watched", watched));
        return (client, libraryId, watched);
    }

    private static Task<long> SeedFileAsync(WeirTestServer server, long libraryId, string relativePath, string status) =>
        TestDatabase.ScalarAsync(
            server,
            "INSERT INTO files (library_id, relative_path, status, last_seen_at) VALUES ($lib, $path, $status, CURRENT_TIMESTAMP) RETURNING id",
            ("$lib", libraryId), ("$path", relativePath), ("$status", status));

    private static async Task<JsonNode> RequeueAsync(ApiTestClient client, long fileId)
    {
        using var response = await client.PostAsync(
            $"/api/v1/processing/files/{fileId}/requeue",
            new { csrf_token = await client.CsrfAsync() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestClient.Json(response);
    }

    [Fact]
    public async Task A_finished_file_whose_original_is_gone_is_not_queued_again()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        var (client, libraryId, _) = await SignInWithALibraryAsync(server);
        var fileId = await SeedFileAsync(server, libraryId, "Heat/heat.mkv", "processed");

        var body = await RequeueAsync(client, fileId);

        Assert.Equal(0, body["requeued"]!.GetValue<int>());
        Assert.Equal(
            "The original of this file is no longer in the watched folder, so Weir has nothing to process again.",
            body["detail"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_finished_file_whose_original_is_still_there_is_queued_again()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        var (client, libraryId, watched) = await SignInWithALibraryAsync(server);
        Directory.CreateDirectory(Path.Join(watched, "Heat"));
        await File.WriteAllTextAsync(Path.Join(watched, "Heat", "heat.mkv"), "not really a film");
        var fileId = await SeedFileAsync(server, libraryId, "Heat/heat.mkv", "cancelled");

        var body = await RequeueAsync(client, fileId);

        Assert.Equal(1, body["requeued"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_bulk_requeue_with_file_ids_queues_only_those_files()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        var (client, libraryId, _) = await SignInWithALibraryAsync(server);
        var chosen = await SeedFileAsync(server, libraryId, "Heat/heat.mkv", "processing_failed");
        var other = await SeedFileAsync(server, libraryId, "Up/up.mkv", "processing_failed");

        using var response = await client.PostAsync(
            "/api/v1/processing/files/requeue",
            new { csrf_token = await client.CsrfAsync(), file_status = "processing_failed", file_ids = new[] { chosen } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await ApiTestClient.Json(response))["requeued"]!.GetValue<int>());
        Assert.Equal(
            "processing_failed",
            await TestDatabase.ScalarStringAsync(server, "SELECT status FROM files WHERE id = $id", ("$id", other)));
    }
}
