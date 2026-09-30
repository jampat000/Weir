using System.Text.Json;
using System.Text.Json.Nodes;
using Weir.Api.Tests.MediaManagers;
using static Weir.Api.Tests.Processing.LibraryFolderChainApiTests;

namespace Weir.Api.Tests.Processing;

/// <summary>
/// The folder chain compared with where download clients really save, over real HTTP with scripted managers and clients:
/// a client whose real folder is outside the watched folder is a problem naming both, a folder Weir can only read as a
/// manager's declaration is reported as not verified rather than ok, and a Weir-only library has no manager or client
/// lines at all.
/// </summary>
public sealed class LibraryFolderChainRealFoldersApiTests
{
    private const string DelunoManifestTemplate = """
        {"product":"Deluno","version":"v1","instanceName":"Deluno","capabilities":["movies","tv","pre-import-processing"],
         "libraries":[{"id":"lib-tv","name":"TV","mediaType":"tv","rootPath":"/media/tv","downloadsPath":DOWNLOADS,
                       "importWorkflow":"refine-before-import","processorOutputPath":OUTPUT}],
         "indexers":[],
         "downloadClients":[{"id":"c1","name":"Transmission","protocol":"torrent","moviesCategory":"deluno-movies","tvCategory":"deluno-tv","isEnabled":true},
                            {"id":"c2","name":"Deluge","protocol":"torrent","moviesCategory":"deluno-movies","tvCategory":"deluno-tv","isEnabled":true}],
         "connections":[]}
        """;

    private static string DelunoManifest(TempFolders folders, string downloads) =>
        DelunoManifestTemplate
            .Replace("DOWNLOADS", JsonSerializer.Serialize(downloads), StringComparison.Ordinal)
            .Replace("OUTPUT", JsonSerializer.Serialize(folders.Output), StringComparison.Ordinal);

    private static string TransmissionSavingTo(string folder) =>
        "{\"arguments\":{\"download-dir\":" + JsonSerializer.Serialize(folder) + "},\"result\":\"success\"}";

    private static IEnumerable<(string State, string Text)> Lines(JsonNode entry) =>
        entry["lines"]!.AsArray().Select(line => (line!["state"]!.GetValue<string>(), line["text"]!.GetValue<string>()));

    private const string TransmissionAddress = "http://192.0.2.41:9091";

    private const string SonarrAddress = "http://192.0.2.60:8989";

    /// <summary>Scripts Sonarr (reached at 192.0.2.60) to say it uses Transmission at <see cref="TransmissionAddress"/>.</summary>
    private static void SonarrUsesTransmission(ScriptedManager manager) =>
        ScriptManagerClients(manager, new Dictionary<string, string[]> { ["192.0.2.60"] = [ManagerClientJson("Transmission", "192.0.2.41", 9091)] });

    [Fact]
    public async Task A_deluno_linked_workflow_is_not_judged_against_a_client_that_does_not_save_into_its_watched_folder()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        using var folders = TempFolders.Create();
        var delunoId = await ConnectAsync(client, "deluno", "Deluno", "http://192.0.2.10:5099");
        manager.Json(HttpMethod.Get, "/api/integrations/external/manifest", DelunoManifest(folders, folders.Watched));
        await ConnectDownloadClientAsync(client, "transmission", "Transmission", TransmissionAddress);
        manager.Json(HttpMethod.Post, "/transmission/rpc", TransmissionSavingTo(Path.GetDirectoryName(folders.Watched)!));
        var libraryId = await CreateLibraryAsync(client, "Deluno and a Transmission", folders, [delunoId]);

        var chain = await FolderChainAsync(client, libraryId);

        var deluno = Assert.Single(chain["managers"]!.AsArray())!;
        Assert.True(deluno["ready"]!.GetValue<bool>());
        Assert.Contains(Lines(deluno), line => line.State == "unverified" && line.Text == $"Deluno on 192.0.2.10 says its TV library downloads to {folders.Watched} (inside Weir's watched folder). Weir can't see where each download client really saves.");
        Assert.DoesNotContain(Lines(deluno), line => line.State == "ok" && line.Text.Contains("downloads", StringComparison.Ordinal));
        Assert.Equal(2, Lines(deluno).Count(line => line.State == "unverified" && line.Text.Contains("files this workflow's downloads under the category \"deluno-tv\"", StringComparison.Ordinal)));
        Assert.Empty(chain["download_clients"]!.AsArray());
        Assert.True(chain["ready"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_client_the_linked_sonarr_uses_that_saves_above_the_watched_folder_is_a_problem_naming_both_folders()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        using var folders = TempFolders.Create();
        var completedRoot = Path.GetDirectoryName(folders.Watched)!;
        var sonarrId = await ConnectAsync(client, "sonarr", "Sonarr", SonarrAddress);
        await ConnectDownloadClientAsync(client, "transmission", "Transmission", TransmissionAddress);
        SonarrUsesTransmission(manager);
        manager.Json(HttpMethod.Post, "/transmission/rpc", TransmissionSavingTo(completedRoot));
        var libraryId = await CreateLibraryAsync(client, "Sonarr and a Transmission", folders, [sonarrId]);

        var chain = await FolderChainAsync(client, libraryId);

        var transmission = Assert.Single(chain["download_clients"]!.AsArray())!;
        Assert.False(transmission["ready"]!.GetValue<bool>());
        var problem = Assert.Single(Lines(transmission), line => line.State == "problem");
        Assert.Contains(completedRoot, problem.Text, StringComparison.Ordinal);
        Assert.Contains(folders.Watched, problem.Text, StringComparison.Ordinal);
        Assert.False(chain["ready"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_weir_only_workflow_is_not_told_about_a_connected_download_client_that_saves_elsewhere()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        using var folders = TempFolders.Create();
        await ConnectDownloadClientAsync(client, "transmission", "Transmission", TransmissionAddress);
        manager.Json(HttpMethod.Post, "/transmission/rpc", TransmissionSavingTo(Path.GetDirectoryName(folders.Watched)!));
        var libraryId = await CreateLibraryAsync(client, "Manual inbox", folders);

        var chain = await FolderChainAsync(client, libraryId);

        Assert.Empty(chain["download_clients"]!.AsArray());
        Assert.True(chain["ready"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_client_saving_into_the_watched_folder_is_listed_for_a_weir_only_workflow_and_others_are_not()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        using var folders = TempFolders.Create();
        await ConnectDownloadClientAsync(client, "sabnzbd", "SABnzbd", "http://192.0.2.40:8080");
        await ConnectDownloadClientAsync(client, "transmission", "Transmission", TransmissionAddress);
        manager.Json(HttpMethod.Get, "/api", "{\"config\":{\"misc\":{\"complete_dir\":" + JsonSerializer.Serialize(folders.Watched) + "}}}")
            .Json(HttpMethod.Post, "/transmission/rpc", TransmissionSavingTo(Path.GetDirectoryName(folders.Watched)!));
        var libraryId = await CreateLibraryAsync(client, "Downloads from SABnzbd", folders);

        var chain = await FolderChainAsync(client, libraryId);

        var sabnzbd = Assert.Single(chain["download_clients"]!.AsArray())!;
        Assert.Equal("sabnzbd", sabnzbd["kind"]!.GetValue<string>());
        Assert.True(sabnzbd["ready"]!.GetValue<bool>());
        Assert.Equal("ok", Assert.Single(Lines(sabnzbd)).State);
    }

    [Fact]
    public async Task A_workflow_is_only_judged_against_the_clients_its_own_manager_uses()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        using var folders = TempFolders.Create();
        var sonarrId = await ConnectAsync(client, "sonarr", "Sonarr", SonarrAddress);
        await ConnectAsync(client, "radarr", "Radarr", "http://192.0.2.61:7878");
        await ConnectDownloadClientAsync(client, "sabnzbd", "SABnzbd", "http://192.0.2.40:8080");
        await ConnectDownloadClientAsync(client, "transmission", "Transmission", TransmissionAddress);
        ScriptManagerClients(manager, new Dictionary<string, string[]>
        {
            ["192.0.2.60"] = [ManagerClientJson("Transmission", "192.0.2.41", 9091)],
            ["192.0.2.61"] = [ManagerClientJson("Sabnzbd", "192.0.2.40", 8080)],
        });
        manager.Json(HttpMethod.Get, "/api", JsonSerializer.Serialize(new { config = new { misc = new { complete_dir = folders.Output } } }))
            .Json(HttpMethod.Post, "/transmission/rpc", TransmissionSavingTo(folders.Work));
        var libraryId = await CreateLibraryAsync(client, "TV from Sonarr", folders, [sonarrId], mediaType: "tv");

        var chain = await FolderChainAsync(client, libraryId);

        var listed = Assert.Single(chain["download_clients"]!.AsArray())!;
        Assert.Equal("transmission", listed["kind"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_client_path_weir_cannot_see_is_not_verified_and_never_a_problem()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        using var folders = TempFolders.Create();
        var sonarrId = await ConnectAsync(client, "sonarr", "Sonarr", SonarrAddress);
        await ConnectDownloadClientAsync(client, "transmission", "Transmission", TransmissionAddress);
        SonarrUsesTransmission(manager);
        manager.Json(HttpMethod.Post, "/transmission/rpc", TransmissionSavingTo("/downloads/tv"));
        var libraryId = await CreateLibraryAsync(client, "Sonarr and a containerised Transmission", folders, [sonarrId]);

        var chain = await FolderChainAsync(client, libraryId);

        var transmission = Assert.Single(chain["download_clients"]!.AsArray())!;
        Assert.True(transmission["ready"]!.GetValue<bool>());
        var line = Assert.Single(Lines(transmission));
        Assert.Equal("unverified", line.State);
        Assert.Contains("Weir cannot see all of that from this computer", line.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_client_that_does_not_answer_is_not_verified_rather_than_left_out_or_passed()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        using var folders = TempFolders.Create();
        var sonarrId = await ConnectAsync(client, "sonarr", "Sonarr", SonarrAddress);
        await ConnectDownloadClientAsync(client, "transmission", "Transmission", TransmissionAddress);
        SonarrUsesTransmission(manager);
        var libraryId = await CreateLibraryAsync(client, "Sonarr and a silent Transmission", folders, [sonarrId]);

        var chain = await FolderChainAsync(client, libraryId);

        var transmission = Assert.Single(chain["download_clients"]!.AsArray())!;
        var line = Assert.Single(Lines(transmission));
        Assert.Equal("unverified", line.State);
        Assert.Contains("did not say where it saves", line.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sonarr_clients_get_a_line_each_with_a_directory_checked_and_a_category_not_verified()
    {
        var (server, client, manager) = await StartAsync();
        await using var _server = server;
        using var folders = TempFolders.Create();
        var sonarrId = await ConnectAsync(client, "sonarr", "Sonarr", "http://192.0.2.60:8989");
        var watched = JsonSerializer.Serialize(folders.Watched);
        var elsewhere = JsonSerializer.Serialize(Path.Join(Path.GetDirectoryName(folders.Watched)!, "elsewhere"));
        manager.Json(HttpMethod.Get, "/api/v3/remotepathmapping", "[]")
            .Json(HttpMethod.Get, "/api/v3/config/downloadclient", """{"enableCompletedDownloadHandling":true,"id":1}""")
            .Json(
                HttpMethod.Get,
                "/api/v3/downloadclient",
                $$"""
                [{"enable":true,"protocol":"torrent","name":"Transmission","fields":[{"name":"host","value":"transmission"},{"name":"tvDirectory","value":{{watched}}}]},
                 {"enable":true,"protocol":"torrent","name":"Deluge","fields":[{"name":"host","value":"deluge"},{"name":"completedDirectory","value":{{elsewhere}}}]},
                 {"enable":true,"protocol":"torrent","name":"qBittorrent","fields":[{"name":"host","value":"qbittorrent"},{"name":"tvCategory","value":"tv-sonarr"}]}]
                """);
        var libraryId = await CreateLibraryAsync(client, "Sonarr clients", folders, [sonarrId]);

        var chain = await FolderChainAsync(client, libraryId);

        var lines = Lines(Assert.Single(chain["managers"]!.AsArray())!).ToList();
        Assert.Single(lines, line => line.State == "ok" && line.Text.StartsWith("Transmission saves", StringComparison.Ordinal));
        Assert.Single(lines, line => line.State == "problem" && line.Text.StartsWith("Deluge saves", StringComparison.Ordinal));
        Assert.Single(lines, line => line.State == "unverified" && line.Text.StartsWith("qBittorrent files", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_weir_only_library_has_no_manager_and_no_download_client_lines()
    {
        var (server, client, _) = await StartAsync();
        await using var _server = server;
        using var folders = TempFolders.Create();
        var libraryId = await CreateLibraryAsync(client, "Weir only", folders);

        var chain = await FolderChainAsync(client, libraryId);

        Assert.Empty(chain["managers"]!.AsArray());
        Assert.Empty(chain["download_clients"]!.AsArray());
        Assert.True(chain["ready"]!.GetValue<bool>());
    }
}
