using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;
using static Weir.Contract.Tests.MediaManagers.DelunoSyncRig;

namespace Weir.Contract.Tests.MediaManagers;

/// <summary>
/// What the workflow sync does when Deluno changes under it or tells it too little: a library that stops processing with Weir
/// or goes away is reported and its workflow left as it is, a missing folder is asked for rather than guessed, a workflow someone
/// linked by hand is taken over rather than duplicated, and a Deluno that does not answer costs nothing.
/// </summary>
[ContractArea("media_managers")]
public sealed class WorkflowSyncEdgeCaseTests
{
    private static IReadOnlyDictionary<string, string> SyncOnWithHandoffs { get; } =
        new Dictionary<string, string>(SyncOn.Concat(Handoffs.SecretEnvironment));

    private static void ListLibraries(FakeManager fake, params JsonObject[] libraries) => fake.Libraries.Replace(libraries);

    private static JsonObject TvLibrary(TemporaryFolder rig) => Library("lib-tv", "TV", "tv", TvReady(rig));

    [Fact]
    public async Task A_library_Deluno_stops_refining_is_reported_in_Activity_and_its_workflow_is_left_as_it_is()
    {
        using var rig = new TemporaryFolder();
        using var fake = DelunoWith(rig, "Completed/Movies");
        await using var server = await WeirServer.StartNewAsync(SyncOn);
        using var client = await server.CreateAdminClientAsync();
        var deluno = await ConnectAsync(client, fake);
        var movies = Movies(await SyncedAsync(client));
        var before = Held(await WorkflowAsync(client, (long)movies["id"]!));
        var name = (string)deluno["name"]!;

        ListLibraries(fake, Library("lib-movies", "Movies", "movie", MoviesReady(rig), importWorkflow: "standard"), TvLibrary(rig));
        await TestConnectionAsync(client, deluno);

        var notice = await NoticeAsync(client, $"{name} no longer hands Movies to Weir");
        var detail = notice["detail"]!.ToString();
        Assert.Contains("is no longer set to Refine before import", detail, StringComparison.Ordinal);
        Assert.Contains("Weir left Movies as it is.", detail, StringComparison.Ordinal);
        Assert.Contains("Choose Refine before import for that library in", detail, StringComparison.Ordinal);
        Assert.Equal(before, Held(await WorkflowAsync(client, (long)movies["id"]!)));
        Assert.Equal(2, (await ProcessingLibraries.ListAsync(client)).Count(row => row!["discovered_library_key"] is not null));
    }

    [Fact]
    public async Task A_library_removed_from_Deluno_is_reported_in_Activity_and_its_workflow_is_left_as_it_is()
    {
        using var rig = new TemporaryFolder();
        using var fake = DelunoWith(rig, "Completed/Movies");
        await using var server = await WeirServer.StartNewAsync(SyncOn);
        using var client = await server.CreateAdminClientAsync();
        var deluno = await ConnectAsync(client, fake);
        var movies = Movies(await SyncedAsync(client));
        var before = Held(await WorkflowAsync(client, (long)movies["id"]!));
        var name = (string)deluno["name"]!;

        ListLibraries(fake, TvLibrary(rig));
        await TestConnectionAsync(client, deluno);

        var notice = await NoticeAsync(client, $"{name} no longer has the library for Movies");
        var detail = notice["detail"]!.ToString();
        Assert.Contains("Weir left Movies as it is.", detail, StringComparison.Ordinal);
        Assert.Contains("Remove this workflow if the library is gone for good, or unlink it from", detail, StringComparison.Ordinal);
        Assert.Equal(before, Held(await WorkflowAsync(client, (long)movies["id"]!)));
        Assert.Equal(2, (await ProcessingLibraries.ListAsync(client)).Count(row => row!["discovered_library_key"] is not null));
    }

    [Fact]
    public async Task A_library_with_no_folder_anywhere_is_linked_with_no_watched_folder_and_Activity_says_what_to_set()
    {
        using var rig = new TemporaryFolder();
        using var fake = FakeManager.StartDeluno([Library("lib-movies", "Movies", "movie", MoviesReady(rig))]);
        fake.Route("GET", DestinationsPath, new JsonObject { ["libraries"] = new JsonArray() });
        await using var server = await WeirServer.StartNewAsync(SyncOn);
        using var client = await server.CreateAdminClientAsync();

        var deluno = await ConnectAsync(client, fake);

        var movies = Movies(await SyncedAsync(client, expected: 1));
        Assert.Equal(string.Empty, (string)movies["watched_folder"]!);
        Assert.Equal(MoviesReady(rig), (string)movies["output_folder"]!);
        Assert.Equal("lib-movies", (string)movies["discovered_library_key"]!);
        Assert.Equal((long)deluno["id"]!, (long)movies["folders_synced_from_connection_id"]!);
        var notice = await NoticeAsync(client, $"{(string)deluno["name"]!} has not told Weir all of the folders for Movies yet");
        var detail = notice["detail"]!.ToString();
        Assert.Contains("say where downloads for Movies arrive", detail, StringComparison.Ordinal);
        Assert.Contains("Set the downloads folder in", detail, StringComparison.Ordinal);
        Assert.All(
            (await ProcessingLibraries.ListAsync(client)).Where(row => (string?)row!["discovered_library_key"] is null),
            row => Assert.Equal(string.Empty, (string)row!["watched_folder"]!));
    }

    [Fact]
    public async Task A_workflow_linked_to_Deluno_by_hand_with_its_folders_is_taken_over_not_duplicated()
    {
        using var rig = new TemporaryFolder();
        using var fake = DelunoWith(rig, "Completed/Movies");
        await using var server = await WeirServer.StartNewAsync(SyncOn);
        using var client = await server.CreateAdminClientAsync();
        var deluno = await ConnectAsync(client, fake, new JsonObject { ["enabled"] = false });
        var folders = new LibraryFolders(Native(rig, "Completed/Movies"), Native(rig, "Ready/Movies"));
        var byHand = await ProcessingLibraries.CreateAsync(
            client, "Hand linked", "movie", folders, new JsonObject { ["manager_connection_ids"] = new JsonArray(JsonFields.Id(deluno)) });
        var workflowsBefore = (await ProcessingLibraries.ListAsync(client)).Count;

        var enabled = await client.PutWithCsrfAsync($"{ManagerConnections.Route}/{JsonFields.Id(deluno)}", new JsonObject { ["enabled"] = true });
        Assert.True(enabled.Status == HttpStatusCode.OK, enabled.ToString());

        var workflows = await SyncedAsync(client);
        Assert.Equal(workflowsBefore, workflows.Count);
        var taken = Assert.Single(workflows, row => (string?)row!["discovered_library_key"] == "lib-movies")!;
        Assert.Equal((long)byHand["id"]!, (long)taken["id"]!);
        Assert.Equal((long)deluno["id"]!, (long)taken["folders_synced_from_connection_id"]!);
        Assert.Equal(folders.Watched, (string)taken["watched_folder"]!);
        Assert.Equal(folders.Output, (string)taken["output_folder"]!);
    }

    [Fact]
    public async Task A_Deluno_that_stops_answering_changes_no_workflow_and_hand_offs_are_still_taken()
    {
        using var rig = new TemporaryFolder();
        using var fake = DelunoWith(rig, "Completed/Movies");
        await using var server = await WeirServer.StartNewAsync(SyncOnWithHandoffs);
        using var client = await server.CreateAdminClientAsync();
        var deluno = await ConnectAsync(client, fake);
        var workflows = await SyncedAsync(client);
        var before = workflows.Select(row => Held(row!.AsObject())).ToList();
        var asked = fake.RequestsTo("GET", Manifest).Count;

        fake.Route("GET", Manifest, new JsonObject { ["message"] = "down" }, status: 503);
        fake.Route("GET", "/api/integrations/external/health", new JsonObject { ["message"] = "down" }, status: 503);
        var saved = await client.PutWithCsrfAsync($"{ManagerConnections.Route}/{JsonFields.Id(deluno)}", new JsonObject { ["enabled"] = true });
        Assert.True(saved.Status == HttpStatusCode.OK, saved.ToString());
        await fake.WaitForRequestAsync("GET", Manifest, asked + 1);

        Assert.Equal(before, (await ProcessingLibraries.ListAsync(client)).Select(row => Held(row!.AsObject())).ToList());
        Assert.Empty(await SyncNoticesAsync(client));
        var handoffId = Handoffs.NewId();
        var source = Path.Join(Native(rig, "Completed/Movies"), handoffId, "film.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await File.WriteAllBytesAsync(source, "x"u8.ToArray());
        var handedOver = await Handoffs.HandOffResponseAsync(server, handoffId, source);
        Assert.True(handedOver.Status == HttpStatusCode.OK, handedOver.ToString());
        Assert.Equal("queued", (string)(await Handoffs.StatusAsync(server, handoffId))["state"]!);
    }
}
