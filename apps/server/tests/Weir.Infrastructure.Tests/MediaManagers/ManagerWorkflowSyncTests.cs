using System.Net;
using System.Text.Json;
using Weir.Core.Activity;
using Weir.Core.Processing;
using Weir.Infrastructure.MediaManagers;

namespace Weir.Infrastructure.Tests.MediaManagers;

/// <summary>
/// <see cref="ManagerWorkflowSync"/> against a scripted Deluno: the install's Movies and TV become Deluno's workflows, stay in
/// step with it, and are otherwise left as the person set them.
/// </summary>
public sealed class ManagerWorkflowSyncTests
{
    private const string Manifest = "/api/integrations/external/manifest";
    private const string Destinations = "/api/integrations/processors/download-destinations";

    private sealed record Library(
        string Id, string Name, string MediaType, string Downloads = "", string Output = "", string Workflow = "refine-before-import");

    private static readonly Library Movies = new("lib-movies", "Movies", "movie", Output: "/data/ready/movies");
    private static readonly Library Tv = new("lib-tv", "TV", "tv", Output: "/data/ready/tv");

    private static string ManifestJson(params Library[] libraries) => JsonSerializer.Serialize(new
    {
        libraries = libraries.Select(library => new Dictionary<string, object?>
        {
            ["id"] = library.Id,
            ["name"] = library.Name,
            ["mediaType"] = library.MediaType,
            ["rootPath"] = "/data/library/" + library.Id,
            ["importWorkflow"] = library.Workflow,
            ["processorOutputPath"] = library.Output,
            ["downloadsPath"] = library.Downloads,
        }),
    });

    private static string DestinationsJson(IReadOnlyDictionary<string, string?> saveFolders, (string Deluno, string Weir)[] mappings) => JsonSerializer.Serialize(new
    {
        libraries = saveFolders.Select(pair => new Dictionary<string, object?>
        {
            ["libraryId"] = pair.Key,
            ["libraryName"] = pair.Key,
            ["destinations"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["downloadClientName"] = "qBittorrent",
                    ["category"] = "weir",
                    ["categoryKind"] = "category",
                    ["saveFolder"] = pair.Value,
                    ["savedBy"] = "client-category",
                    ["status"] = "ok",
                    ["message"] = string.Empty,
                },
            },
            ["processorConnection"] = new
            {
                pathMappings = mappings.Select(mapping => new { delunoPath = mapping.Deluno, processorPath = mapping.Weir }),
            },
        }),
    });

    private static MediaManagerFixture Deluno(Library[] libraries, Dictionary<string, string?>? saveFolders = null, (string, string)[]? mappings = null)
    {
        var fixture = new MediaManagerFixture();
        Script(fixture, libraries, saveFolders ?? libraries.ToDictionary(library => library.Id, library => (string?)("/data/completed/" + library.MediaType)), mappings ?? []);
        return fixture;
    }

    private static void Script(MediaManagerFixture fixture, Library[] libraries, Dictionary<string, string?> saveFolders, (string, string)[] mappings) =>
        fixture.Http
            .Json(HttpMethod.Get, Manifest, ManifestJson(libraries))
            .Json(HttpMethod.Get, Destinations, DestinationsJson(saveFolders, mappings));

    private static async Task<long> ConnectAsync(MediaManagerFixture fixture) =>
        await fixture.AddConnectionAsync("deluno", "http://192.0.2.30:5099", "k1");

    private static Task Sync(MediaManagerFixture fixture) => fixture.WorkflowSync.SyncAllAsync(ManagerWorkflowSync.ScheduledTrigger, CancellationToken.None);

    private static Task<List<ProcessingLibraryRecord>> Workflows(MediaManagerFixture fixture) =>
        fixture.Db(uow => fixture.Libraries.ListAsync(uow));

    private static Task<List<string>> Titles(MediaManagerFixture fixture, string eventType) =>
        fixture.Db(uow => uow.QueryAsync(
            "SELECT title FROM activity_events WHERE event_type = $type ORDER BY id", reader => reader.GetString(0), ("$type", eventType)));

    [Fact]
    public async Task The_install_defaults_become_Delunos_workflows_with_its_folders_and_a_link()
    {
        using var fixture = Deluno([Movies, Tv], mappings: [("/data", "/mnt/media")]);
        var connection = await ConnectAsync(fixture);

        await Sync(fixture);

        var workflows = await Workflows(fixture);
        Assert.Equal(2, workflows.Count);
        var movies = workflows.Single(workflow => workflow.MediaType == "movie");
        Assert.Equal("Movies", movies.Name);
        Assert.Equal("/mnt/media/completed/movie", movies.WatchedFolder);
        Assert.Equal("/mnt/media/ready/movies", movies.OutputFolder);
        Assert.Equal(connection, movies.DiscoveredFromConnectionId);
        Assert.Equal("lib-movies", movies.DiscoveredLibraryKey);
        Assert.Equal([connection], await fixture.Db(uow => fixture.Libraries.ManagerConnectionIdsAsync(uow, movies.Id)));
        var tv = workflows.Single(workflow => workflow.MediaType == "tv");
        Assert.Equal("/mnt/media/completed/tv", tv.WatchedFolder);
        Assert.Equal("lib-tv", tv.DiscoveredLibraryKey);
    }

    [Fact]
    public async Task Setting_up_leaves_the_work_folder_and_the_rules_profile_alone_and_records_one_event_each()
    {
        using var fixture = Deluno([Movies, Tv]);
        await ConnectAsync(fixture);
        var before = (await Workflows(fixture)).Single(workflow => workflow.MediaType == "movie");
        await fixture.Store.Execute($"UPDATE libraries SET work_folder = '/scratch/work' WHERE id = {before.Id}");

        await Sync(fixture);

        var movies = (await Workflows(fixture)).Single(workflow => workflow.MediaType == "movie");
        Assert.Equal("/scratch/work", movies.WorkFolder);
        Assert.Equal(before.RuleSetId, movies.RuleSetId);
        Assert.Equal(before.Enabled, movies.Enabled);
        Assert.Equal(["Workflow Movies set up from Deluno on 192.0.2.30", "Workflow TV set up from Deluno on 192.0.2.30"], await Titles(fixture, ActivityEventTypes.ProcessingWorkflowSynced));
    }

    [Fact]
    public async Task A_second_sync_with_nothing_changed_changes_and_says_nothing()
    {
        using var fixture = Deluno([Movies, Tv]);
        await ConnectAsync(fixture);
        await Sync(fixture);
        var first = await Workflows(fixture);

        await Sync(fixture);

        Assert.Equal(first, await Workflows(fixture));
        Assert.Equal(2, (await Titles(fixture, ActivityEventTypes.ProcessingWorkflowSynced)).Count);
    }

    [Fact]
    public async Task A_library_named_differently_renames_the_workflow_it_adopts()
    {
        using var fixture = Deluno([Movies with { Name = "Films" }]);
        await ConnectAsync(fixture);

        await Sync(fixture);

        Assert.Contains(await Workflows(fixture), workflow => workflow is { Name: "Films", MediaType: "movie", DiscoveredLibraryKey: "lib-movies" });
        Assert.Equal(["Workflow Films set up from Deluno on 192.0.2.30"], await Titles(fixture, ActivityEventTypes.ProcessingWorkflowSynced));
    }

    [Fact]
    public async Task A_changed_downloads_folder_updates_only_that_folder_and_says_so()
    {
        using var fixture = Deluno([Movies]);
        await ConnectAsync(fixture);
        await Sync(fixture);
        Script(fixture, [Movies], new Dictionary<string, string?> { ["lib-movies"] = "/data/new-completed/movies" }, []);

        await Sync(fixture);

        var movies = Assert.Single(await Workflows(fixture), workflow => workflow.MediaType == "movie");
        Assert.Equal("/data/new-completed/movies", movies.WatchedFolder);
        Assert.Equal("/data/ready/movies", movies.OutputFolder);
        Assert.Equal("Movies' watched folder updated from Deluno on 192.0.2.30", (await Titles(fixture, ActivityEventTypes.ProcessingWorkflowSynced))[^1]);
    }

    [Fact]
    public async Task A_workflow_with_folders_of_its_own_is_not_adopted_and_the_library_gets_a_new_one()
    {
        using var fixture = Deluno([Movies]);
        await ConnectAsync(fixture);
        await fixture.Store.Execute("UPDATE libraries SET watched_folder = '/mine/in', output_folder = '/mine/out' WHERE media_type = 'movie'");

        await Sync(fixture);

        var workflows = await Workflows(fixture);
        Assert.Equal(3, workflows.Count);
        Assert.Equal("/mine/in", workflows.Single(workflow => workflow.DiscoveredLibraryKey is null && workflow.MediaType == "movie").WatchedFolder);
        var created = workflows.Single(workflow => workflow.DiscoveredLibraryKey == "lib-movies");
        Assert.Equal("Movies (2)", created.Name);
        Assert.Equal("/data/completed/movie", created.WatchedFolder);
        Assert.True(created.Enabled);
        Assert.NotNull(created.RuleSetId);
    }

    [Fact]
    public async Task A_library_that_does_not_process_with_Weir_gets_no_workflow()
    {
        using var fixture = Deluno([Movies with { Workflow = "standard" }]);
        await ConnectAsync(fixture);

        await Sync(fixture);

        Assert.All(await Workflows(fixture), workflow => Assert.Null(workflow.DiscoveredLibraryKey));
        Assert.Empty(await Titles(fixture, ActivityEventTypes.ProcessingWorkflowSynced));
    }

    [Fact]
    public async Task A_library_that_stops_processing_with_Weir_leaves_its_workflow_exactly_as_it_was()
    {
        using var fixture = Deluno([Movies]);
        await ConnectAsync(fixture);
        await Sync(fixture);
        var before = await Workflows(fixture);
        Script(fixture, [Movies with { Workflow = "standard" }], new Dictionary<string, string?> { ["lib-movies"] = "/elsewhere" }, []);

        await Sync(fixture);

        Assert.Equal(before, await Workflows(fixture));
    }

    [Fact]
    public async Task A_library_Deluno_no_longer_has_leaves_its_workflow_and_deletes_nothing()
    {
        using var fixture = Deluno([Movies, Tv]);
        await ConnectAsync(fixture);
        await Sync(fixture);
        var before = await Workflows(fixture);
        Script(fixture, [Tv], new Dictionary<string, string?> { ["lib-tv"] = "/data/completed/tv" }, []);

        await Sync(fixture);

        Assert.Equal(before, await Workflows(fixture));
    }

    [Fact]
    public async Task Unlinking_a_workflow_stops_the_sync_for_it_and_makes_no_second_workflow()
    {
        using var fixture = Deluno([Movies]);
        var connection = await ConnectAsync(fixture);
        await Sync(fixture);
        var movies = (await Workflows(fixture)).Single(workflow => workflow.DiscoveredLibraryKey == "lib-movies");
        await fixture.Store.Execute($"DELETE FROM library_manager_links WHERE library_id = {movies.Id} AND connection_id = {connection}");
        Script(fixture, [Movies], new Dictionary<string, string?> { ["lib-movies"] = "/data/new/movies" }, []);

        await Sync(fixture);

        var after = await Workflows(fixture);
        Assert.Equal(2, after.Count);
        Assert.Equal("/data/completed/movie", after.Single(workflow => workflow.Id == movies.Id).WatchedFolder);
    }

    [Fact]
    public async Task A_Deluno_that_does_not_answer_changes_nothing_and_throws_nothing()
    {
        using var fixture = new MediaManagerFixture();
        fixture.Http.Throw(HttpMethod.Get, Manifest, new HttpRequestException("connection refused"));
        await ConnectAsync(fixture);
        var before = await Workflows(fixture);

        await Sync(fixture);

        Assert.Equal(before, await Workflows(fixture));
        Assert.Empty(await Titles(fixture, ActivityEventTypes.ProcessingWorkflowSynced));
    }

    [Fact]
    public async Task A_key_Deluno_refuses_changes_nothing()
    {
        using var fixture = new MediaManagerFixture();
        fixture.Http.Json(HttpMethod.Get, Manifest, """{"error":"no"}""", HttpStatusCode.Unauthorized);
        await ConnectAsync(fixture);
        var before = await Workflows(fixture);

        await Sync(fixture);

        Assert.Equal(before, await Workflows(fixture));
    }

    [Fact]
    public async Task A_Deluno_without_download_destinations_is_set_up_from_its_manifest_alone()
    {
        using var fixture = new MediaManagerFixture();
        fixture.Http
            .Json(HttpMethod.Get, Manifest, ManifestJson(Movies with { Downloads = "/data/downloads/movies" }))
            .Json(HttpMethod.Get, Destinations, "{}", HttpStatusCode.NotFound);
        await ConnectAsync(fixture);

        await Sync(fixture);

        var movies = Assert.Single(await Workflows(fixture), workflow => workflow.DiscoveredLibraryKey == "lib-movies");
        Assert.Equal("/data/downloads/movies", movies.WatchedFolder);
        Assert.Equal("/data/ready/movies", movies.OutputFolder);
    }

    [Fact]
    public async Task A_library_with_no_downloads_folder_anywhere_is_linked_with_no_watched_folder_and_asks_once_for_it()
    {
        using var fixture = new MediaManagerFixture();
        fixture.Http
            .Json(HttpMethod.Get, Manifest, ManifestJson(Movies))
            .Json(HttpMethod.Get, Destinations, "{}", HttpStatusCode.NotFound);
        await ConnectAsync(fixture);

        await Sync(fixture);
        await Sync(fixture);

        var movies = Assert.Single(await Workflows(fixture), workflow => workflow.DiscoveredLibraryKey == "lib-movies");
        Assert.Equal(string.Empty, movies.WatchedFolder);
        Assert.Equal("/data/ready/movies", movies.OutputFolder);
        Assert.Equal(["Deluno on 192.0.2.30 has not told Weir all of the folders for Movies yet"], await Titles(fixture, ActivityEventTypes.ProcessingWorkflowSyncNotice));
        var detail = await fixture.Db(uow => uow.QueryAsync("SELECT detail FROM activity_events WHERE event_type = $t", reader => reader.GetString(0), ("$t", ActivityEventTypes.ProcessingWorkflowSyncNotice)));
        Assert.Contains(
            "Deluno on 192.0.2.30 doesn't say where downloads for Movies arrive. Set the downloads folder in Deluno on 192.0.2.30 (or the clients' category folders) and Weir will pick it up.",
            Assert.Single(detail),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Folders_that_overlap_another_workflow_are_not_applied_and_the_person_is_told()
    {
        using var fixture = Deluno([Movies]);
        await ConnectAsync(fixture);
        await fixture.Store.Execute("UPDATE libraries SET watched_folder = '/data/completed', output_folder = '/mine/out' WHERE media_type = 'tv'");

        await Sync(fixture);

        var movies = Assert.Single(await Workflows(fixture), workflow => workflow.MediaType == "movie");
        Assert.Equal(string.Empty, movies.WatchedFolder);
        Assert.Null(movies.DiscoveredLibraryKey);
        Assert.Single(await Titles(fixture, ActivityEventTypes.ProcessingWorkflowSyncNotice));
    }

    [Fact]
    public async Task Managers_that_cannot_report_folders_are_never_asked()
    {
        using var fixture = new MediaManagerFixture();
        await fixture.AddConnectionAsync("sonarr", "http://192.0.2.31:8989", "k2");
        var before = await Workflows(fixture);

        await Sync(fixture);

        Assert.Empty(fixture.Http.Requests);
        Assert.Equal(before, await Workflows(fixture));
    }

    [Fact]
    public async Task A_disabled_connection_is_not_asked()
    {
        using var fixture = Deluno([Movies]);
        await fixture.AddConnectionAsync("deluno", "http://192.0.2.30:5099", "k1", enabled: false);

        await Sync(fixture);

        Assert.Empty(fixture.Http.Requests);
    }

    [Fact]
    public async Task A_request_runs_the_sync_in_the_background_and_returns_at_once()
    {
        using var fixture = Deluno([Movies]);
        await ConnectAsync(fixture);

        fixture.WorkflowSync.RequestSync();

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline && (await Workflows(fixture)).All(workflow => workflow.DiscoveredLibraryKey is null))
        {
            await Task.Delay(50);
        }

        Assert.Contains(await Workflows(fixture), workflow => workflow.DiscoveredLibraryKey == "lib-movies");
    }
}
