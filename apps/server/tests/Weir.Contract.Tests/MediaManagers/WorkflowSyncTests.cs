using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;

namespace Weir.Contract.Tests.MediaManagers;

/// <summary>
/// Connecting Deluno sets up Weir's workflows from it. The server runs with the sync on (the harness turns it off so a
/// connection never changes a workflow behind another test's back) against a fake Deluno that, like a fresh install,
/// has no downloads folder of its own and says where its clients save instead.
/// </summary>
[ContractArea("media_managers")]
public sealed class WorkflowSyncTests
{
    private const string Libraries = $"{WeirClient.Api}/processing/libraries";
    private const string DestinationsPath = "/api/integrations/processors/download-destinations";

    private static readonly IReadOnlyDictionary<string, string> SyncOn = new Dictionary<string, string> { [ServerEnvironment.WorkflowSync] = "1" };

    private static JsonObject Library(string id, string name, string mediaType, string output) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["mediaType"] = mediaType,
        ["rootPath"] = "/library/" + id,
        ["importWorkflow"] = "refine-before-import",
        ["processorOutputPath"] = output,
        ["downloadsPath"] = string.Empty,
    };

    private static JsonObject Published(string id, string name, string saveFolder, string output) => new()
    {
        ["libraryId"] = id,
        ["libraryName"] = name,
        ["downloadsPath"] = string.Empty,
        ["processorOutputPath"] = output,
        ["destinations"] = new JsonArray(new JsonObject
        {
            ["downloadClientName"] = "qBittorrent",
            ["category"] = "weir",
            ["categoryKind"] = "category",
            ["saveFolder"] = saveFolder,
            ["savedBy"] = "client-category",
            ["status"] = "ok",
            ["message"] = string.Empty,
        }),
        ["processorConnection"] = new JsonObject { ["pathMappings"] = new JsonArray() },
    };

    private static FakeManager DelunoWith(TemporaryFolder rig, string moviesSaveFolder)
    {
        var moviesReady = Directory.CreateDirectory(Path.Join(rig.Path, "Ready", "Movies")).FullName;
        var tvReady = Directory.CreateDirectory(Path.Join(rig.Path, "Ready", "TV")).FullName;
        var fake = FakeManager.StartDeluno([Library("lib-movies", "Movies", "movie", moviesReady), Library("lib-tv", "TV", "tv", tvReady)]);
        PublishDestinations(fake, rig, moviesSaveFolder);
        return fake;
    }

    private static void PublishDestinations(FakeManager fake, TemporaryFolder rig, string moviesSaveFolder) =>
        fake.Route("GET", DestinationsPath, new JsonObject
        {
            ["libraries"] = new JsonArray(
                Published("lib-movies", "Movies", Directory.CreateDirectory(Path.Join(rig.Path, moviesSaveFolder)).FullName, Path.Join(rig.Path, "Ready", "Movies")),
                Published("lib-tv", "TV", Directory.CreateDirectory(Path.Join(rig.Path, "Completed", "TV")).FullName, Path.Join(rig.Path, "Ready", "TV"))),
        });

    private static async Task<JsonArray> SyncedAsync(WeirClient client) =>
        await Poll.UntilAsync(
            async () =>
            {
                var listed = await client.GetAsync(Libraries);
                Assert.True(listed.Status == HttpStatusCode.OK, listed.ToString());
                var rows = listed.Elements;
                return rows.Count(row => row!["discovered_library_key"] is not null) >= 2 ? rows : null;
            },
            "both workflows to be set up from Deluno");

    /// <summary>A folder under the rig, written the way this machine writes paths.</summary>
    private static string Native(TemporaryFolder rig, string relative) => Path.Join(rig.Path, relative.Replace('/', Path.DirectorySeparatorChar));

    private static JsonObject Movies(JsonArray workflows) =>
        Assert.Single(workflows, row => (string)row!["media_type"]! == "movie")!.AsObject();

    [Fact]
    public async Task Connecting_Deluno_fills_in_both_workflows_with_nothing_typed_and_leaves_the_folder_chain_ready()
    {
        using var rig = new TemporaryFolder();
        using var fake = DelunoWith(rig, "Completed/Movies");
        await using var server = await WeirServer.StartNewAsync(SyncOn);
        using var client = await server.CreateAdminClientAsync();

        var created = await ManagerConnections.CreateAsync(client, new JsonObject { ["base_url"] = fake.BaseUrl, ["api_key"] = fake.ApiKey });
        Assert.True(created.Status == HttpStatusCode.Created, created.ToString());
        var delunoId = (long)created.Fields["id"]!;

        var workflows = await SyncedAsync(client);

        Assert.Equal(2, workflows.Count);
        foreach (var (mediaType, saved, ready) in new[] { ("movie", "Completed/Movies", "Ready/Movies"), ("tv", "Completed/TV", "Ready/TV") })
        {
            var workflow = Assert.Single(workflows, row => (string)row!["media_type"]! == mediaType)!;
            Assert.Equal(Native(rig, saved), (string)workflow["watched_folder"]!);
            Assert.Equal(Native(rig, ready), (string)workflow["output_folder"]!);
            Assert.Equal(string.Empty, (string)workflow["work_folder"]!);
            Assert.Equal([delunoId], workflow["manager_connection_ids"]!.AsArray().Select(id => (long)id!));
            Assert.Equal(delunoId, (long)workflow["folders_synced_from_connection_id"]!);
            var chain = await client.GetAsync($"{Libraries}/{(long)workflow["id"]!}/folder-chain");
            Assert.True(chain.Status == HttpStatusCode.OK && (bool)chain.Fields["ready"]!, chain.ToString());
        }
    }

    [Fact]
    public async Task A_folder_Deluno_changes_reaches_the_workflow_after_a_connection_test_until_the_workflow_is_unlinked()
    {
        using var rig = new TemporaryFolder();
        using var fake = DelunoWith(rig, "Completed/Movies");
        await using var server = await WeirServer.StartNewAsync(SyncOn);
        using var client = await server.CreateAdminClientAsync();
        var created = await ManagerConnections.CreateAsync(client, new JsonObject { ["base_url"] = fake.BaseUrl, ["api_key"] = fake.ApiKey });
        var delunoId = (long)created.Fields["id"]!;
        var movies = Movies(await SyncedAsync(client));
        var moviesId = (long)movies["id"]!;

        PublishDestinations(fake, rig, "Completed/Movies 4K");
        var test = await client.PostWithCsrfAsync($"{ManagerConnections.Route}/{delunoId}/test", new JsonObject());
        Assert.True(test.Status == HttpStatusCode.OK, test.ToString());
        var moved = Path.Join(rig.Path, "Completed", "Movies 4K");
        await Poll.UntilAsync(
            async () => (string)(await client.GetAsync($"{Libraries}/{moviesId}")).Fields["watched_folder"]! == moved,
            "the workflow to follow Deluno's new folder");

        var unlinked = await client.PostWithCsrfAsync($"{Libraries}/{moviesId}/unlink", new JsonObject());
        Assert.True(unlinked.Status == HttpStatusCode.OK, unlinked.ToString());
        Assert.Null(unlinked.Fields["folders_synced_from_connection_id"]);
        PublishDestinations(fake, rig, "Completed/Movies 8K");
        var again = await client.PostWithCsrfAsync($"{ManagerConnections.Route}/{delunoId}/test", new JsonObject());
        Assert.True(again.Status == HttpStatusCode.OK, again.ToString());
        await fake.WaitForRequestAsync("GET", DestinationsPath, 3);
        await Task.Delay(500);

        Assert.Equal(moved, (string)(await client.GetAsync($"{Libraries}/{moviesId}")).Fields["watched_folder"]!);
    }
}
