using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Weir.Api.Tests.MediaManagers;
using Weir.Api.Tests.Platform;
using Weir.Infrastructure.MediaManagers;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.Processing;

/// <summary>
/// The #768 folder-chain endpoints over real HTTP: <c>GET /processing/libraries/{id}/folder-chain</c> and
/// <c>GET /media-managers/connections/{id}/folder-chain</c>, against real temp directories on disk (so
/// <see cref="FilesystemFolderProbe"/> runs for real) and a scripted Sonarr/Radarr/Deluno for the manager half. Covers
/// #768's four named combinations: Weir only, Sonarr + Weir (and Radarr + Weir, the same case for movies), Deluno +
/// Weir, and all three managers on one library.
/// </summary>
public sealed class LibraryFolderChainApiTests
{
    private const string DelunoManifest = """
        {"product":"Deluno","version":"v1","instanceName":"Deluno","capabilities":["movies","tv","pre-import-processing"],
         "libraries":[{"id":"lib-tv","name":"TV","mediaType":"tv","rootPath":"/media/tv","downloadsPath":"WATCHED",
                       "importWorkflow":"refine-before-import","processorOutputPath":"OUTPUT","processorTimeoutMinutes":0,
                       "processorFailureMode":"import-original","missingSearchEnabled":true,"upgradeSearchEnabled":true,"maxItemsPerRun":10,
                       "automationStatus":"idle","cleanupMode":"keep-source","removeEmptySourceFolders":false}],
         "indexers":[],"downloadClients":[],"connections":[]}
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

    private static async Task<long> CreateLibraryAsync(
        ApiTestClient client, string name, TempFolders folders, IReadOnlyList<long>? connectionIds = null, string mediaType = "tv")
    {
        using var response = await client.PostAsync("/api/v1/processing/libraries", new Dictionary<string, object?>
        {
            ["csrf_token"] = await client.CsrfAsync(),
            ["name"] = name,
            ["media_type"] = mediaType,
            ["watched_folder"] = folders.Watched,
            ["work_folder"] = folders.Work,
            ["output_folder"] = folders.Output,
            ["manager_connection_ids"] = connectionIds ?? [],
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response))!["id"]!.GetValue<long>();
    }

    private static async Task<JsonNode> FolderChainAsync(ApiTestClient client, long libraryId)
    {
        using var response = await client.GetAsync($"/api/v1/processing/libraries/{libraryId}/folder-chain");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response))!;
    }

    private sealed record TempFolders(string Watched, string Work, string Output) : IDisposable
    {
        public static TempFolders Create()
        {
            var root = Path.Join(Path.GetTempPath(), "weir-folder-chain-" + Guid.NewGuid().ToString("N"));
            var folders = new TempFolders(Path.Join(root, "watched"), Path.Join(root, "work"), Path.Join(root, "output"));
            Directory.CreateDirectory(folders.Watched);
            Directory.CreateDirectory(folders.Work);
            Directory.CreateDirectory(folders.Output);
            return folders;
        }

        public void Dispose()
        {
            var root = Path.GetDirectoryName(Watched)!;
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task A_weir_only_library_is_ready_with_no_manager_and_no_absence_warning()
    {
        var (server, client, _) = await StartAsync();
        await using var _server = server;
        using var folders = TempFolders.Create();
        var libraryId = await CreateLibraryAsync(client, "Weir only", folders);

        var chain = await FolderChainAsync(client, libraryId);

        Assert.Equal(libraryId, chain["library_id"]!.GetValue<long>());
        Assert.True(chain["local"]!["ready"]!.GetValue<bool>());
        Assert.All(chain["local"]!["lines"]!.AsArray(), line => Assert.NotEqual("problem", line!["state"]!.GetValue<string>()));
        Assert.Empty(chain["managers"]!.AsArray());
        Assert.True(chain["ready"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_sonarr_and_weir_library_surfaces_sonarrs_own_lines()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        using var folders = TempFolders.Create();
        var connectionId = await ConnectAsync(client, "sonarr", "Sonarr", "http://192.0.2.60:8989");
        manager.Json(HttpMethod.Get, "/api/v3/remotepathmapping", "[]")
            .Json(HttpMethod.Get, "/api/v3/downloadclient", "[]")
            .Json(HttpMethod.Get, "/api/v3/config/downloadclient", """{"enableCompletedDownloadHandling":true,"id":1}""");
        var libraryId = await CreateLibraryAsync(client, "Sonarr and Weir", folders, [connectionId]);

        var chain = await FolderChainAsync(client, libraryId);

        Assert.True(chain["local"]!["ready"]!.GetValue<bool>());
        var sonarr = Assert.Single(chain["managers"]!.AsArray())!;
        Assert.Equal("sonarr", sonarr["kind"]!.GetValue<string>());
        Assert.False(sonarr["ready"]!.GetValue<bool>());
        Assert.Contains(sonarr["lines"]!.AsArray(), line => line!["text"]!.GetValue<string>().Contains("no enabled download client", StringComparison.Ordinal));
        // The manager side is not ready, so the combined chain is not ready even though Weir's own side is.
        Assert.False(chain["ready"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_radarr_and_weir_library_surfaces_radarrs_own_lines()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        using var folders = TempFolders.Create();
        var connectionId = await ConnectAsync(client, "radarr", "Radarr", "http://192.0.2.61:7878");
        manager.Json(HttpMethod.Get, "/api/v3/remotepathmapping", "[]")
            .Json(HttpMethod.Get, "/api/v3/downloadclient", "[]")
            .Json(HttpMethod.Get, "/api/v3/config/downloadclient", """{"enableCompletedDownloadHandling":true,"id":1}""");
        var libraryId = await CreateLibraryAsync(client, "Radarr and Weir", folders, [connectionId], mediaType: "movie");

        var chain = await FolderChainAsync(client, libraryId);

        Assert.True(chain["local"]!["ready"]!.GetValue<bool>());
        var radarr = Assert.Single(chain["managers"]!.AsArray())!;
        Assert.Equal("radarr", radarr["kind"]!.GetValue<string>());
        Assert.False(radarr["ready"]!.GetValue<bool>());
        Assert.Contains(radarr["lines"]!.AsArray(), line => line!["text"]!.GetValue<string>().Contains("no enabled download client", StringComparison.Ordinal));
        Assert.False(chain["ready"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_deluno_and_weir_library_surfaces_delunos_own_lines()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        using var folders = TempFolders.Create();
        var connectionId = await ConnectAsync(client, "deluno", "Deluno", "http://192.0.2.10:5099");
        manager.Json(HttpMethod.Get, "/api/integrations/external/manifest", DelunoManifest.Replace("WATCHED", folders.Watched.Replace("\\", "\\\\")).Replace("OUTPUT", folders.Output.Replace("\\", "\\\\")));
        var libraryId = await CreateLibraryAsync(client, "Deluno and Weir", folders, [connectionId]);

        var chain = await FolderChainAsync(client, libraryId);

        Assert.True(chain["local"]!["ready"]!.GetValue<bool>());
        var deluno = Assert.Single(chain["managers"]!.AsArray())!;
        Assert.Equal("deluno", deluno["kind"]!.GetValue<string>());
        Assert.True(deluno["ready"]!.GetValue<bool>());
        Assert.NotEmpty(deluno["lines"]!.AsArray());
        Assert.True(chain["ready"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_library_linked_to_sonarr_radarr_and_deluno_gets_one_entry_per_manager()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        using var folders = TempFolders.Create();
        var sonarrId = await ConnectAsync(client, "sonarr", "Sonarr", "http://192.0.2.60:8989");
        var radarrId = await ConnectAsync(client, "radarr", "Radarr", "http://192.0.2.61:7878");
        var delunoId = await ConnectAsync(client, "deluno", "Deluno", "http://192.0.2.10:5099");
        manager.Json(HttpMethod.Get, "/api/v3/remotepathmapping", "[]")
            .Json(HttpMethod.Get, "/api/v3/downloadclient", "[]")
            .Json(HttpMethod.Get, "/api/v3/config/downloadclient", """{"enableCompletedDownloadHandling":true,"id":1}""")
            .Json(HttpMethod.Get, "/api/integrations/external/manifest", DelunoManifest.Replace("WATCHED", folders.Watched.Replace("\\", "\\\\")).Replace("OUTPUT", folders.Output.Replace("\\", "\\\\")));
        var libraryId = await CreateLibraryAsync(client, "All three", folders, [sonarrId, radarrId, delunoId]);

        var chain = await FolderChainAsync(client, libraryId);

        var managers = chain["managers"]!.AsArray();
        Assert.Equal(2, managers.Count); // Radarr covers movies only, so it does not appear for a TV library.
        Assert.Contains(managers, item => item!["kind"]!.GetValue<string>() == "sonarr");
        Assert.Contains(managers, item => item!["kind"]!.GetValue<string>() == "deluno");
    }

    [Fact]
    public async Task The_connection_folder_chain_endpoint_lists_every_library_linked_to_it()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        using var foldersA = TempFolders.Create();
        using var foldersB = TempFolders.Create();
        var connectionId = await ConnectAsync(client, "sonarr", "Sonarr", "http://192.0.2.60:8989");
        manager.Json(HttpMethod.Get, "/api/v3/remotepathmapping", "[]")
            .Json(HttpMethod.Get, "/api/v3/downloadclient", "[]")
            .Json(HttpMethod.Get, "/api/v3/config/downloadclient", """{"enableCompletedDownloadHandling":true,"id":1}""");
        var libraryA = await CreateLibraryAsync(client, "Library A", foldersA, [connectionId]);
        var libraryB = await CreateLibraryAsync(client, "Library B", foldersB, [connectionId]);
        using var foldersUnrelated = TempFolders.Create();
        await CreateLibraryAsync(client, "Unrelated", foldersUnrelated);

        using var response = await client.GetAsync($"/api/v1/media-managers/connections/{connectionId}/folder-chain");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await Json(response))!.AsArray();
        Assert.Equal(2, body.Count);
        Assert.Equal([libraryA, libraryB], body.Select(item => item!["library_id"]!.GetValue<long>()));
    }

    [Fact]
    public async Task An_unknown_connection_id_is_not_found_on_the_connection_folder_chain_endpoint()
    {
        var (server, client, _) = await StartAsync();
        await using var _server = server;

        using var response = await client.GetAsync("/api/v1/media-managers/connections/999999/folder-chain");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>The connection folder-chain route's sibling reads (list/get a connection) need an operator or
    /// admin session, like every other media-manager connections route that is not a plain library read.</summary>
    [Fact]
    public async Task A_viewer_is_refused_the_connection_folder_chain_endpoint()
    {
        var (server, client, _) = await StartAsync();
        await using var _server = server;
        var connectionId = await ConnectAsync(client, "sonarr", "Sonarr", "http://192.0.2.60:8989");
        await TestDatabase.SeedViewerAsync(server);
        var viewer = new ApiTestClient(server);
        await viewer.SignInAsync("bob", ViewerPassword);

        using var response = await viewer.GetAsync($"/api/v1/media-managers/connections/{connectionId}/folder-chain");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_library_with_no_folders_set_yet_gets_one_plain_line_not_a_crash()
    {
        var (server, client, _) = await StartAsync();
        await using var _server = server;

        using var response = await client.PostAsync("/api/v1/processing/libraries", new Dictionary<string, object?>
        {
            ["csrf_token"] = await client.CsrfAsync(),
            ["name"] = "Still being set up",
            ["media_type"] = "tv",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var libraryId = (await Json(response))!["id"]!.GetValue<long>();

        var chain = await FolderChainAsync(client, libraryId);

        Assert.False(chain["local"]!["ready"]!.GetValue<bool>());
        var line = Assert.Single(chain["local"]!["lines"]!.AsArray())!;
        Assert.Equal("problem", line["state"]!.GetValue<string>());
        Assert.False(chain["ready"]!.GetValue<bool>());
    }

    [Fact]
    public async Task An_unknown_library_id_is_not_found()
    {
        var (server, client, _) = await StartAsync();
        await using var _server = server;

        using var response = await client.GetAsync("/api/v1/processing/libraries/999999/folder-chain");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<long> ConnectDownloadClientAsync(ApiTestClient client, string kind, string name, string baseUrl)
    {
        using var response = await client.PostAsync("/api/v1/download-clients/connections", new Dictionary<string, object?>
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

    [Fact]
    public async Task A_download_client_whose_completed_folder_is_the_watched_folder_is_ready()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        using var folders = TempFolders.Create();
        await ConnectDownloadClientAsync(client, "sabnzbd", "SABnzbd", "http://192.0.2.40:8080");
        var completeDir = System.Text.Json.JsonSerializer.Serialize(folders.Watched);
        manager.Json(HttpMethod.Get, "/api", "{\"config\":{\"misc\":{\"complete_dir\":" + completeDir + "}}}");
        var libraryId = await CreateLibraryAsync(client, "Weir with SABnzbd", folders);

        var chain = await FolderChainAsync(client, libraryId);

        var sabnzbd = Assert.Single(chain["download_clients"]!.AsArray())!;
        Assert.Equal("sabnzbd", sabnzbd["kind"]!.GetValue<string>());
        Assert.True(sabnzbd["ready"]!.GetValue<bool>());
        Assert.Contains(sabnzbd["lines"]!.AsArray(), line => line!["text"]!.GetValue<string>().Contains("completed-downloads folder", StringComparison.Ordinal));
        Assert.True(chain["ready"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_download_client_with_no_matching_folder_makes_the_chain_not_ready()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        using var folders = TempFolders.Create();
        await ConnectDownloadClientAsync(client, "sabnzbd", "SABnzbd", "http://192.0.2.40:8080");
        manager.Json(HttpMethod.Get, "/api", """{"config":{"misc":{"complete_dir":"/somewhere/else"}}}""");
        var libraryId = await CreateLibraryAsync(client, "Weir with a mismatched SABnzbd", folders);

        var chain = await FolderChainAsync(client, libraryId);

        var sabnzbd = Assert.Single(chain["download_clients"]!.AsArray())!;
        Assert.False(sabnzbd["ready"]!.GetValue<bool>());
        Assert.Contains(sabnzbd["lines"]!.AsArray(), line => line!["state"]!.GetValue<string>() == "problem");
        Assert.False(chain["ready"]!.GetValue<bool>());
    }
}
