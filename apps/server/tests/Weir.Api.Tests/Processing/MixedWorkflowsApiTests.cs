using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Weir.Api.Tests.MediaManagers;
using Weir.Api.Tests.Platform;
using Weir.Infrastructure.MediaManagers;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.Processing;

/// <summary>
/// Weir-only workflows and workflows linked to a media manager on one install, over real HTTP: each one's folder chain
/// only involves the managers it is linked to, the folder rules keep every workflow's folders apart, and a manager
/// only ever hears about the workflows it feeds.
/// </summary>
public sealed class MixedWorkflowsApiTests
{
    private const string DelunoManifest = """
        {"product":"Deluno","version":"v1","instanceName":"Deluno","capabilities":["movies","tv","pre-import-processing"],
         "libraries":[{"id":"lib-tv","name":"TV","mediaType":"tv","rootPath":"/media/tv","downloadsPath":"WATCHED",
                       "importWorkflow":"refine-before-import","processorOutputPath":"OUTPUT","processorTimeoutMinutes":0,
                       "processorFailureMode":"import-original","missingSearchEnabled":true,"upgradeSearchEnabled":true,"maxItemsPerRun":10,
                       "automationStatus":"idle","cleanupMode":"keep-source","removeEmptySourceFolders":false}],
         "indexers":[],"downloadClients":[],"connections":[]}
        """;

    private const string ArrClients = """
        [{"enable":true,"protocol":"usenet","name":"SABnzbd","fields":[{"name":"host","value":"sabnzbd"}],
          "implementationName":"SABnzbd","implementation":"Sabnzbd","id":1}]
        """;

    private static async Task<(WeirTestServer Server, ApiTestClient Client, ScriptedManager Manager)> StartAsync()
    {
        var manager = new ScriptedManager();
        var server = await WeirTestServer.StartAsync(
            [("WEIR_SESSION_SECRET", Secret), ("WEIR_PROCESSING_WORKER_COUNT", "0")],
            configureServices: services => services.AddSingleton<IManagerHttpHandlerFactory>(manager));
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        return (server, client, manager);
    }

    private sealed record Folders(string Root) : IDisposable
    {
        public string Watched => Path.Join(Root, "watched");

        public string Output => Path.Join(Root, "output");

        public static Folders Create()
        {
            var folders = new Folders(Path.Join(Path.GetTempPath(), "weir-mixed-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(folders.Watched);
            Directory.CreateDirectory(folders.Output);
            return folders;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task<long> ConnectAsync(ApiTestClient client, string kind, string name, string baseUrl)
    {
        using var response = await client.PostAsync("/api/v1/media-managers/connections", new Dictionary<string, object?>
        {
            ["csrf_token"] = await client.CsrfAsync(),
            ["kind"] = kind,
            ["name"] = name,
            ["base_url"] = baseUrl,
            ["api_key"] = "key",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response))!["id"]!.GetValue<long>();
    }

    private static async Task<HttpResponseMessage> PostWorkflowAsync(
        ApiTestClient client, string name, string mediaType, Folders folders, IReadOnlyList<long> linkedTo) =>
        await client.PostAsync("/api/v1/processing/libraries", new Dictionary<string, object?>
        {
            ["csrf_token"] = await client.CsrfAsync(),
            ["name"] = name,
            ["media_type"] = mediaType,
            ["watched_folder"] = folders.Watched,
            ["output_folder"] = folders.Output,
            ["manager_connection_ids"] = linkedTo,
        });

    private static async Task<long> CreateWorkflowAsync(
        ApiTestClient client, string name, string mediaType, Folders folders, params long[] linkedTo)
    {
        using var response = await PostWorkflowAsync(client, name, mediaType, folders, linkedTo);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response))!["id"]!.GetValue<long>();
    }

    private static async Task<JsonNode> ChainAsync(ApiTestClient client, long libraryId)
    {
        using var response = await client.GetAsync($"/api/v1/processing/libraries/{libraryId}/folder-chain");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response))!;
    }

    private static IEnumerable<string> ManagerKinds(JsonNode chain) =>
        chain["managers"]!.AsArray().Select(item => item!["kind"]!.GetValue<string>());

    private static string Escaped(string path) => path.Replace("\\", "\\\\", StringComparison.Ordinal);

    [Fact]
    public async Task A_linked_workflow_is_saved_with_the_rejected_file_choice_and_original_setting_it_was_given()
    {
        var (server, client, _) = await StartAsync();
        await using var _server = server;
        using var folders = Folders.Create();
        var delunoId = await ConnectAsync(client, "deluno", "Deluno", "http://192.0.2.10:5099");

        using var created = await client.PostAsync("/api/v1/processing/libraries", new Dictionary<string, object?>
        {
            ["csrf_token"] = await client.CsrfAsync(),
            ["name"] = "TV from Deluno",
            ["media_type"] = "tv",
            ["watched_folder"] = folders.Watched,
            ["output_folder"] = folders.Output,
            ["manager_connection_ids"] = new[] { delunoId },
            ["rejected_file_action"] = "delete_file",
            ["remove_original_after_success"] = true,
        });

        // Whether the original goes is decided from the links when a file is processed, so what was chosen is kept as it was sent.
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var workflow = await Json(created);
        Assert.Equal("delete_file", workflow["rejected_file_action"]!.GetValue<string>());
        Assert.True(workflow["remove_original_after_success"]!.GetValue<bool>());
        using var read = await client.GetAsync($"/api/v1/processing/libraries/{workflow["id"]!.GetValue<long>()}");
        Assert.Equal("delete_file", (await Json(read))["rejected_file_action"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_weir_only_workflow_next_to_a_deluno_one_is_ready_without_involving_deluno()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        using var linked = Folders.Create();
        using var local = Folders.Create();
        var delunoId = await ConnectAsync(client, "deluno", "Deluno", "http://192.0.2.10:5099");
        manager.Json(HttpMethod.Get, "/api/integrations/external/manifest", DelunoManifest.Replace("WATCHED", Escaped(linked.Watched), StringComparison.Ordinal).Replace("OUTPUT", Escaped(linked.Output), StringComparison.Ordinal));
        var linkedId = await CreateWorkflowAsync(client, "TV from Deluno", "tv", linked, delunoId);
        var localId = await CreateWorkflowAsync(client, "Kids", "tv", local);

        var linkedChain = await ChainAsync(client, linkedId);
        var localChain = await ChainAsync(client, localId);

        Assert.Equal(["deluno"], ManagerKinds(linkedChain));
        Assert.True(linkedChain["ready"]!.GetValue<bool>());
        Assert.Empty(ManagerKinds(localChain));
        Assert.True(localChain["ready"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_weir_only_workflow_next_to_a_sonarr_one_is_not_held_to_sonarrs_mapping()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        using var linked = Folders.Create();
        using var local = Folders.Create();
        var sonarrId = await ConnectAsync(client, "sonarr", "Sonarr", "http://192.0.2.60:8989");
        manager.Json(HttpMethod.Get, "/api/v3/remotepathmapping", "[]")
            .Json(HttpMethod.Get, "/api/v3/downloadclient", ArrClients)
            .Json(HttpMethod.Get, "/api/v3/config/downloadclient", """{"enableCompletedDownloadHandling":true,"id":1}""");
        var linkedId = await CreateWorkflowAsync(client, "TV from Sonarr", "tv", linked, sonarrId);
        var localId = await CreateWorkflowAsync(client, "Home videos", "tv", local);

        var linkedChain = await ChainAsync(client, linkedId);
        var localChain = await ChainAsync(client, localId);

        Assert.Equal(["sonarr"], ManagerKinds(linkedChain));
        Assert.False(linkedChain["ready"]!.GetValue<bool>());
        Assert.Empty(ManagerKinds(localChain));
        Assert.True(localChain["ready"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Weir_only_deluno_and_sonarr_radarr_workflows_together_each_see_only_their_own_managers()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        using var delunoFolders = Folders.Create();
        using var sonarrFolders = Folders.Create();
        using var radarrFolders = Folders.Create();
        using var local = Folders.Create();
        var delunoId = await ConnectAsync(client, "deluno", "Deluno", "http://192.0.2.10:5099");
        var sonarrId = await ConnectAsync(client, "sonarr", "Sonarr", "http://192.0.2.60:8989");
        var radarrId = await ConnectAsync(client, "radarr", "Radarr", "http://192.0.2.61:7878");
        manager.Json(HttpMethod.Get, "/api/integrations/external/manifest", DelunoManifest.Replace("WATCHED", Escaped(delunoFolders.Watched), StringComparison.Ordinal).Replace("OUTPUT", Escaped(delunoFolders.Output), StringComparison.Ordinal))
            .Json(HttpMethod.Get, "/api/v3/remotepathmapping", "[]")
            .Json(HttpMethod.Get, "/api/v3/downloadclient", ArrClients)
            .Json(HttpMethod.Get, "/api/v3/config/downloadclient", """{"enableCompletedDownloadHandling":true,"id":1}""");
        var delunoTv = await CreateWorkflowAsync(client, "TV from Deluno", "tv", delunoFolders, delunoId);
        var sonarrTv = await CreateWorkflowAsync(client, "TV from Sonarr", "tv", sonarrFolders, sonarrId);
        var radarrMovies = await CreateWorkflowAsync(client, "Movies from Radarr", "movie", radarrFolders, radarrId);
        var localMovies = await CreateWorkflowAsync(client, "Weir only movies", "movie", local);

        Assert.Equal(["deluno"], ManagerKinds(await ChainAsync(client, delunoTv)));
        Assert.Equal(["sonarr"], ManagerKinds(await ChainAsync(client, sonarrTv)));
        Assert.Equal(["radarr"], ManagerKinds(await ChainAsync(client, radarrMovies)));
        Assert.Empty(ManagerKinds(await ChainAsync(client, localMovies)));
    }

    [Fact]
    public async Task A_connection_lists_only_the_workflows_it_feeds()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        using var linked = Folders.Create();
        using var local = Folders.Create();
        var sonarrId = await ConnectAsync(client, "sonarr", "Sonarr", "http://192.0.2.60:8989");
        manager.Json(HttpMethod.Get, "/api/v3/remotepathmapping", "[]")
            .Json(HttpMethod.Get, "/api/v3/downloadclient", ArrClients)
            .Json(HttpMethod.Get, "/api/v3/config/downloadclient", """{"enableCompletedDownloadHandling":true,"id":1}""");
        var linkedId = await CreateWorkflowAsync(client, "TV from Sonarr", "tv", linked, sonarrId);
        await CreateWorkflowAsync(client, "Weir only TV", "tv", local);

        using var response = await client.GetAsync($"/api/v1/media-managers/connections/{sonarrId}/folder-chain");

        var chains = (await Json(response))!.AsArray();
        Assert.Equal([linkedId], chains.Select(chain => chain!["library_id"]!.GetValue<long>()));
    }

    [Fact]
    public async Task The_manager_setup_check_can_be_narrowed_to_the_managers_a_workflow_is_linked_to()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        var sonarrId = await ConnectAsync(client, "sonarr", "Sonarr", "http://192.0.2.60:8989");
        await ConnectAsync(client, "deluno", "Deluno", "http://192.0.2.10:5099");
        manager.Json(HttpMethod.Get, "/api/v3/remotepathmapping", "[]")
            .Json(HttpMethod.Get, "/api/v3/downloadclient", ArrClients)
            .Json(HttpMethod.Get, "/api/v3/config/downloadclient", """{"enableCompletedDownloadHandling":true,"id":1}""")
            .Json(HttpMethod.Get, "/api/integrations/external/manifest", DelunoManifest);
        const string Path = "/api/v1/processing/manager-setup?media_type=tv&watched_folder=%2Fa&output_folder=%2Fb";

        var everyManager = (await Json(await client.GetAsync(Path)))!["managers"]!.AsArray();
        var onlySonarr = (await Json(await client.GetAsync($"{Path}&connection_ids={sonarrId}")))!["managers"]!.AsArray();
        var none = (await Json(await client.GetAsync($"{Path}&connection_ids=")))!["managers"]!.AsArray();

        Assert.Equal(2, everyManager.Count);
        Assert.Equal(["sonarr"], onlySonarr.Select(item => item!["kind"]!.GetValue<string>()));
        Assert.Empty(none);
    }

    [Fact]
    public async Task A_weir_only_workflow_whose_watched_folder_sits_inside_a_linked_ones_is_refused()
    {
        var (server, client, _) = await StartAsync();
        await using var _server = server;
        using var linked = Folders.Create();
        var delunoId = await ConnectAsync(client, "deluno", "Deluno", "http://192.0.2.10:5099");
        await CreateWorkflowAsync(client, "TV from Deluno", "tv", linked, delunoId);
        using var inside = Folders.Create();
        var nested = new Dictionary<string, object?>
        {
            ["csrf_token"] = await client.CsrfAsync(),
            ["name"] = "Nested",
            ["media_type"] = "tv",
            ["watched_folder"] = Path.Join(linked.Watched, "kids"),
            ["output_folder"] = inside.Output,
        };

        using var response = await client.PostAsync("/api/v1/processing/libraries", nested);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("overlaps the watched folder of 'TV from Deluno'", await Detail(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_linked_workflow_whose_output_folder_is_a_weir_only_workflows_watched_folder_is_refused()
    {
        var (server, client, _) = await StartAsync();
        await using var _server = server;
        using var local = Folders.Create();
        using var other = Folders.Create();
        var delunoId = await ConnectAsync(client, "deluno", "Deluno", "http://192.0.2.10:5099");
        await CreateWorkflowAsync(client, "Weir only TV", "tv", local);

        using var response = await client.PostAsync("/api/v1/processing/libraries", new Dictionary<string, object?>
        {
            ["csrf_token"] = await client.CsrfAsync(),
            ["name"] = "TV from Deluno",
            ["media_type"] = "tv",
            ["watched_folder"] = other.Watched,
            ["output_folder"] = local.Watched,
            ["manager_connection_ids"] = new[] { delunoId },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("overlaps the watched folder of 'Weir only TV'", await Detail(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Workflows_with_separate_folders_sit_side_by_side_whatever_they_are_linked_to()
    {
        var (server, client, _) = await StartAsync();
        await using var _server = server;
        using var first = Folders.Create();
        using var second = Folders.Create();
        using var third = Folders.Create();
        var delunoId = await ConnectAsync(client, "deluno", "Deluno", "http://192.0.2.10:5099");
        var sonarrId = await ConnectAsync(client, "sonarr", "Sonarr", "http://192.0.2.60:8989");

        await CreateWorkflowAsync(client, "TV from Deluno", "tv", first, delunoId);
        await CreateWorkflowAsync(client, "TV from Sonarr", "tv", second, sonarrId);
        await CreateWorkflowAsync(client, "Weir only TV", "tv", third);

        using var listed = await client.GetAsync("/api/v1/processing/libraries");
        var linksByName = (await Json(listed))!.AsArray()
            .ToDictionary(item => item!["name"]!.GetValue<string>(), item => item!["manager_connection_ids"]!.AsArray().Count);
        Assert.Equal(1, linksByName["TV from Deluno"]);
        Assert.Equal(1, linksByName["TV from Sonarr"]);
        Assert.Equal(0, linksByName["Weir only TV"]);
    }
}
