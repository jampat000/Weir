using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.MediaManagers.DelunoSyncRig;

namespace Weir.Contract.Tests.MediaManagers;

/// <summary>
/// Connecting Deluno sets up Weir's workflows from it. The server runs with the sync on (the harness turns it off so a
/// connection never changes a workflow behind another test's back) against a fake Deluno that, like a fresh install,
/// has no downloads folder of its own and says where its clients save instead.
/// </summary>
[ContractArea("media_managers")]
public sealed class WorkflowSyncTests
{
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
            var chain = await client.GetAsync($"{LibrariesRoute}/{(long)workflow["id"]!}/folder-chain");
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
            async () => (string)(await client.GetAsync($"{LibrariesRoute}/{moviesId}")).Fields["watched_folder"]! == moved,
            "the workflow to follow Deluno's new folder");

        var unlinked = await client.PostWithCsrfAsync($"{LibrariesRoute}/{moviesId}/unlink", new JsonObject());
        Assert.True(unlinked.Status == HttpStatusCode.OK, unlinked.ToString());
        Assert.Null(unlinked.Fields["folders_synced_from_connection_id"]);
        PublishDestinations(fake, rig, "Completed/Movies 8K");
        var again = await client.PostWithCsrfAsync($"{ManagerConnections.Route}/{delunoId}/test", new JsonObject());
        Assert.True(again.Status == HttpStatusCode.OK, again.ToString());
        await fake.WaitForRequestAsync("GET", DestinationsPath, 3);
        await Task.Delay(500);

        Assert.Equal(moved, (string)(await client.GetAsync($"{LibrariesRoute}/{moviesId}")).Fields["watched_folder"]!);
    }

    [Fact]
    public async Task A_folder_Deluno_changes_is_recorded_in_Activity_in_plain_words()
    {
        using var rig = new TemporaryFolder();
        using var fake = DelunoWith(rig, "Completed/Movies");
        await using var server = await WeirServer.StartNewAsync(SyncOn);
        using var client = await server.CreateAdminClientAsync();
        var deluno = await ConnectAsync(client, fake);
        await SyncedAsync(client);

        PublishDestinations(fake, rig, "Completed/Movies 4K");
        await TestConnectionAsync(client, deluno);

        var updated = await Poll.UntilAsync(
            async () =>
            {
                var events = await client.GetAsync($"{WeirClient.Api}/activity/recent", ("event_type", "processing.workflow_synced"), ("limit", 100));
                Assert.True(events.Status == HttpStatusCode.OK, events.ToString());
                return events.Fields["items"]!.AsArray().Select(item => (string)item!["title"]!).FirstOrDefault(title => title.Contains("updated from", StringComparison.Ordinal));
            },
            "the update to be recorded in Activity");
        Assert.Equal($"Movies' watched folder updated from {(string)deluno["name"]!}", updated);
    }
}
