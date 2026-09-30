using System.Text.Json.Nodes;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.Processing;

/// <summary>
/// What a workflow's editor is told about a media manager or download client must be true and plain. A folder that is
/// not the watched folder is never fine anywhere, and a manager that cannot be reached gets one sentence with its
/// address, never transport text.
/// </summary>
public sealed class ManagerSectionTruthApiTests
{
    private const string DelunoAddress = "http://192.0.2.70:7879";

    private const string SuggestionsPath = "/api/v1/download-clients/suggestions?media_type=movie";

    private static IEnumerable<JsonNode> Lines(JsonNode entry) => entry["lines"]!.AsArray().Select(line => line!);

    [Fact]
    public async Task A_download_client_folder_that_is_not_the_watched_folder_is_offered_but_never_marked_fine()
    {
        var (server, client, manager) = await LibraryFolderChainApiTests.StartAsync();
        await using var _server = server;
        using var folders = LibraryFolderChainApiTests.TempFolders.Create();
        var radarrId = await LibraryFolderChainApiTests.ConnectAsync(client, "radarr", "Radarr", "http://192.0.2.61:7878");
        await LibraryFolderChainApiTests.ConnectDownloadClientAsync(client, "sabnzbd", "SABnzbd", "http://192.0.2.40:8080");
        LibraryFolderChainApiTests.ScriptManagerClients(
            manager, new Dictionary<string, string[]> { ["192.0.2.61"] = [LibraryFolderChainApiTests.ManagerClientJson("Sabnzbd", "192.0.2.40", 8080)] });
        manager.Json(HttpMethod.Get, "/api", System.Text.Json.JsonSerializer.Serialize(new { config = new { misc = new { complete_dir = folders.Output } } }));
        var libraryId = await LibraryFolderChainApiTests.CreateLibraryAsync(client, "Movies with a mismatched SABnzbd", folders, [radarrId], mediaType: "movie");

        var chain = await LibraryFolderChainApiTests.FolderChainAsync(client, libraryId);
        using var suggestionsResponse = await client.GetAsync(SuggestionsPath);
        var suggestion = Assert.Single((await Json(suggestionsResponse))!.AsArray())!;

        var chained = Assert.Single(chain["download_clients"]!.AsArray())!;
        Assert.False(chained["ready"]!.GetValue<bool>());
        Assert.DoesNotContain(Lines(chained), line => line["state"]!.GetValue<string>() == "ok");
        Assert.Contains(Lines(chained), line => line["state"]!.GetValue<string>() == "problem");
        Assert.False(chain["ready"]!.GetValue<bool>());
        Assert.Equal(folders.Output, suggestion["suggested_watched_folder"]!.GetValue<string>());
        Assert.Null(suggestion["ready"]);
        Assert.Null(suggestion["lines"]);
    }

    [Fact]
    public async Task A_stopped_manager_is_told_with_its_address_and_no_transport_text_in_the_chain_and_the_setup_check()
    {
        var (server, client, _) = await LibraryFolderChainApiTests.StartAsync();
        await using var _server = server;
        using var folders = LibraryFolderChainApiTests.TempFolders.Create();
        var connectionId = await LibraryFolderChainApiTests.ConnectAsync(client, "deluno", "Deluno", DelunoAddress);
        var libraryId = await LibraryFolderChainApiTests.CreateLibraryAsync(client, "Movies linked to a stopped Deluno", folders, [connectionId], mediaType: "movie");

        var chain = await LibraryFolderChainApiTests.FolderChainAsync(client, libraryId);
        using var setupResponse = await client.GetAsync(
            $"/api/v1/processing/manager-setup?media_type=movie&watched_folder={Uri.EscapeDataString(folders.Watched)}" +
            $"&output_folder={Uri.EscapeDataString(folders.Output)}&connection_ids={connectionId}");
        var setup = (await Json(setupResponse))!;

        foreach (var entry in new[] { Assert.Single(chain["managers"]!.AsArray())!, Assert.Single(setup["managers"]!.AsArray())! })
        {
            var expected = $"Weir could not reach {entry["label"]!.GetValue<string>()} at {DelunoAddress}. " +
                           "Check the address is right, and that the app is running and reachable from this machine.";
            var line = Assert.Single(Lines(entry));
            Assert.Equal("problem", line["state"]!.GetValue<string>());
            Assert.Equal(expected, line["text"]!.GetValue<string>());
            Assert.False(entry["ready"]!.GetValue<bool>());
        }
    }
}
