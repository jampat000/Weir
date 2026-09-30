using System.Net;
using System.Text.Json.Nodes;

namespace Weir.Api.Tests.Platform;

/// <summary>
/// "Process all again" for rejected files: the whole rejected set goes back to work after a rules change, and only
/// the files whose original is still in the watched folder are queued.
/// </summary>
public sealed class ProcessingRejectedFilesApiTests
{
    private const string SummaryPath = "/api/v1/processing/files/rejected/summary";
    private const string ProcessAgainPath = "/api/v1/processing/files/rejected/process-again";

    private static async Task<long> SeedLibraryAsync(WeirTestServer server, string name)
    {
        var watched = Directory.CreateDirectory(Path.Join(server.Home, "watched-" + name)).FullName;
        return await TestDatabase.ScalarAsync(
            server,
            "INSERT INTO libraries (name, media_type, watched_folder) VALUES ($name, 'movie', $watched) RETURNING id",
            ("$name", name), ("$watched", watched));
    }

    private static async Task<long> SeedFileAsync(WeirTestServer server, long libraryId, string relativePath, string status, bool onDisk)
    {
        if (onDisk)
        {
            var watched = await TestDatabase.ScalarStringAsync(server, "SELECT watched_folder FROM libraries WHERE id = $id", ("$id", libraryId));
            var path = Path.Join(watched, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "not really a film");
        }

        return await TestDatabase.ScalarAsync(
            server,
            "INSERT INTO files (library_id, relative_path, status, last_seen_at) VALUES ($lib, $path, $status, CURRENT_TIMESTAMP) RETURNING id",
            ("$lib", libraryId), ("$path", relativePath), ("$status", status));
    }

    private static Task<string?> StatusOfAsync(WeirTestServer server, long fileId) =>
        TestDatabase.ScalarStringAsync(server, "SELECT status FROM files WHERE id = $id", ("$id", fileId));

    private static async Task<JsonNode> ProcessAgainAsync(ApiTestClient client, long? libraryId = null)
    {
        using var response = await client.PostAsync(ProcessAgainPath, new { csrf_token = await client.CsrfAsync(), library_id = libraryId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestClient.Json(response);
    }

    private static async Task<JsonNode> SummaryAsync(ApiTestClient client, string query = "")
    {
        using var response = await client.GetAsync(SummaryPath + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestClient.Json(response);
    }

    private static async Task<(ApiTestClient Client, long Films, long Shows)> StartWithTwoWorkflowsAsync(WeirTestServer server)
    {
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        return (client, await SeedLibraryAsync(server, "Films"), await SeedLibraryAsync(server, "Shows"));
    }

    [Fact]
    public async Task The_summary_counts_rejected_files_and_those_still_in_the_watched_folder()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        var (client, films, shows) = await StartWithTwoWorkflowsAsync(server);
        await SeedFileAsync(server, films, "Heat/heat.mkv", "rejected", onDisk: true);
        await SeedFileAsync(server, films, "Up/up.mkv", "rejected", onDisk: false);
        await SeedFileAsync(server, shows, "Show/s01e01.mkv", "rejected", onDisk: true);
        await SeedFileAsync(server, films, "Alien/alien.mkv", "processing_failed", onDisk: true);

        var summary = await SummaryAsync(client);

        Assert.Equal(3, summary["rejected"]!.GetValue<int>());
        Assert.Equal(2, summary["ready"]!.GetValue<int>());
    }

    [Fact]
    public async Task The_summary_can_be_narrowed_to_one_workflow()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        var (client, films, shows) = await StartWithTwoWorkflowsAsync(server);
        await SeedFileAsync(server, films, "Heat/heat.mkv", "rejected", onDisk: true);
        await SeedFileAsync(server, shows, "Show/s01e01.mkv", "rejected", onDisk: true);
        await SeedFileAsync(server, shows, "Show/s01e02.mkv", "rejected", onDisk: true);

        var summary = await SummaryAsync(client, $"?library_id={shows}");

        Assert.Equal(2, summary["rejected"]!.GetValue<int>());
        Assert.Equal(2, summary["ready"]!.GetValue<int>());
    }

    [Fact]
    public async Task Processing_all_again_queues_each_rejected_file_that_is_still_in_its_watched_folder()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        var (client, films, shows) = await StartWithTwoWorkflowsAsync(server);
        var heat = await SeedFileAsync(server, films, "Heat/heat.mkv", "rejected", onDisk: true);
        var episode = await SeedFileAsync(server, shows, "Show/s01e01.mkv", "rejected", onDisk: true);

        var body = await ProcessAgainAsync(client);

        Assert.Equal(2, body["requeued"]!.GetValue<int>());
        Assert.Equal(0, body["skipped"]!.GetValue<int>());
        Assert.Equal("unprocessed", await StatusOfAsync(server, heat));
        Assert.Equal("unprocessed", await StatusOfAsync(server, episode));
        Assert.Equal(2, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM jobs WHERE job_kind = 'processing.file.remux_pass.v1'"));
    }

    [Fact]
    public async Task Processing_all_again_skips_and_counts_a_rejected_file_whose_original_is_gone()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        var (client, films, _) = await StartWithTwoWorkflowsAsync(server);
        var present = await SeedFileAsync(server, films, "Heat/heat.mkv", "rejected", onDisk: true);
        var gone = await SeedFileAsync(server, films, "Up/up.mkv", "rejected", onDisk: false);

        var body = await ProcessAgainAsync(client);

        Assert.Equal(1, body["requeued"]!.GetValue<int>());
        Assert.Equal(1, body["skipped"]!.GetValue<int>());
        Assert.Equal("unprocessed", await StatusOfAsync(server, present));
        Assert.Equal("rejected", await StatusOfAsync(server, gone));
    }

    [Fact]
    public async Task Processing_all_again_leaves_files_that_are_not_rejected_alone()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        var (client, films, _) = await StartWithTwoWorkflowsAsync(server);
        await SeedFileAsync(server, films, "Heat/heat.mkv", "rejected", onDisk: true);
        var failed = await SeedFileAsync(server, films, "Alien/alien.mkv", "processing_failed", onDisk: true);
        var processed = await SeedFileAsync(server, films, "Up/up.mkv", "processed", onDisk: true);

        await ProcessAgainAsync(client);

        Assert.Equal("processing_failed", await StatusOfAsync(server, failed));
        Assert.Equal("processed", await StatusOfAsync(server, processed));
    }

    [Fact]
    public async Task Processing_all_again_can_be_narrowed_to_one_workflow()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        var (client, films, shows) = await StartWithTwoWorkflowsAsync(server);
        var film = await SeedFileAsync(server, films, "Heat/heat.mkv", "rejected", onDisk: true);
        var episode = await SeedFileAsync(server, shows, "Show/s01e01.mkv", "rejected", onDisk: true);

        var body = await ProcessAgainAsync(client, libraryId: shows);

        Assert.Equal(1, body["requeued"]!.GetValue<int>());
        Assert.Equal("rejected", await StatusOfAsync(server, film));
        Assert.Equal("unprocessed", await StatusOfAsync(server, episode));
    }

    [Fact]
    public async Task With_nothing_rejected_processing_all_again_queues_nothing()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        var (client, _, _) = await StartWithTwoWorkflowsAsync(server);

        var body = await ProcessAgainAsync(client);

        Assert.Equal(0, body["requeued"]!.GetValue<int>());
        Assert.Equal("Nothing matched, so nothing was queued.", body["detail"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_viewer_cannot_process_rejected_files_again()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedViewerAsync(server);
        var films = await SeedLibraryAsync(server, "Films");
        var rejected = await SeedFileAsync(server, films, "Heat/heat.mkv", "rejected", onDisk: true);
        var viewer = new ApiTestClient(server);
        await viewer.SignInAsync("bob", ApiTestClient.ViewerPassword);

        using var response = await viewer.PostAsync(ProcessAgainPath, new { csrf_token = await viewer.CsrfAsync() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("rejected", await StatusOfAsync(server, rejected));
    }
}
