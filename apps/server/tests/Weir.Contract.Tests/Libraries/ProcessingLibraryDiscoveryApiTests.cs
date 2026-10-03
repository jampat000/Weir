using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;
using static Weir.Contract.Tests.Libraries.LibrariesPartBDiscovery;

namespace Weir.Contract.Tests.Libraries;

/// <summary>Media-manager library discovery, import, drift and unlink (#554), against a fake Deluno manager.</summary>
[ContractArea("libraries")]
public sealed class ProcessingLibraryDiscoveryApiTests(ServerFixture fixture) : IClassFixture<ServerFixture>
{
    private WeirServer Server => fixture.Server;

    // --- GET .../discover/{connection_id} ------------------------------------------------------------

    [Fact]
    public async Task Discover_requires_authentication()
    {
        using var anonymous = Server.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await DiscoverAsync(anonymous, 1)).Status);
    }

    [Fact]
    public async Task Discover_lists_libraries_and_marks_what_is_already_imported()
    {
        using var operatorClient = await OperatorAsync();
        using var folder = new TemporaryFolder();
        var moviesRoot = MakeFolder(folder, "movies");
        var tvRoot = MakeFolder(folder, "tv");
        using var fake = FakeManager.StartDeluno(libraries:
        [
            ManagerLibrary("7", "Films-Disc", "movies", moviesRoot),
            ManagerLibrary("8", "Shows-Disc", "tv", tvRoot),
        ]);
        var connectionId = await ConnectAsync(operatorClient, fake);
        var imported = await ImportAsync(operatorClient, connectionId, "7");
        LibrariesPartBChecks.Status(imported, HttpStatusCode.Created);

        var response = await DiscoverAsync(operatorClient, connectionId);

        LibrariesPartBChecks.Status(response, HttpStatusCode.OK);
        var found = response.Elements.ToDictionary(row => (string)row!["key"]!, row => row!.AsObject());
        Assert.True((bool)found["7"]["already_imported"]!);
        Assert.False((bool)found["8"]["already_imported"]!);
        Assert.Equal("tv", (string)found["8"]["media_type"]!);
    }

    [Fact]
    public async Task Discover_shows_the_output_root_before_the_import()
    {
        using var operatorClient = await OperatorAsync();
        using var folder = new TemporaryFolder();
        var root = MakeFolder(folder, "movies");
        using var fake = FakeManager.StartDeluno(libraries:
        [
            ManagerLibrary("7", "Movies", "movies", root, importWorkflow: "refine-before-import", processorOutputPath: root),
        ]);
        var connectionId = await ConnectAsync(operatorClient, fake);

        var row = (await DiscoverAsync(operatorClient, connectionId)).Elements[0]!.AsObject();

        Assert.True((bool)row["processes_before_import"]!);
        Assert.Equal(root, (string)row["output_path"]!);
        LibrariesPartBChecks.IsNull(row, "output_path_problem");
    }

    [Fact]
    public async Task Discover_for_an_unknown_connection_is_404()
    {
        using var operatorClient = await OperatorAsync();

        var response = await DiscoverAsync(operatorClient, 999999);

        LibrariesPartBChecks.Status(response, HttpStatusCode.NotFound);
        Assert.Equal("That media manager connection does not exist.", (string)response.Fields["detail"]!);
    }

    [Fact]
    public async Task Discover_for_a_connection_with_no_saved_key_is_502()
    {
        using var operatorClient = await OperatorAsync();
        using var fake = FakeManager.StartDeluno();
        var created = await CreateConnectionAsync(operatorClient, fake.BaseUrl, apiKey: string.Empty);
        LibrariesPartBChecks.Status(created, HttpStatusCode.Created);

        var response = await DiscoverAsync(operatorClient, (long)created.Fields["id"]!);

        LibrariesPartBChecks.Status(response, HttpStatusCode.BadGateway);
        Assert.Contains("address and API key", (string)response.Fields["detail"]!, StringComparison.Ordinal);
    }

    // --- POST .../discover/{connection_id}/import ------------------------------------------------------

    [Fact]
    public async Task Import_creates_only_the_chosen_subset_and_adopts_the_visible_root()
    {
        using var operatorClient = await OperatorAsync();
        using var folder = new TemporaryFolder();
        var root = MakeFolder(folder, "lib");
        using var fake = FakeManager.StartDeluno(libraries:
        [
            ManagerLibrary("7", "Films-Subset", "movies", root),
            ManagerLibrary("8", "Shows-Subset", "tv", root),
            ManagerLibrary("9", "Kids-Subset", "movies", root),
        ]);
        var connectionId = await ConnectAsync(operatorClient, fake);

        var response = await ImportAsync(operatorClient, connectionId, "7", "9");

        LibrariesPartBChecks.Status(response, HttpStatusCode.Created);
        var created = response.Elements.Select(row => row!.AsObject()).ToList();
        Assert.Equal(["Films-Subset", "Kids-Subset"], created.Select(row => (string)row["name"]!).Order(StringComparer.Ordinal));
        Assert.Equal(new HashSet<string> { "7", "9" }, created.Select(row => (string)row["discovered_library_key"]!).ToHashSet());
        Assert.All(created, row => Assert.Equal(connectionId, (long)row["discovered_from_connection_id"]!));
        Assert.All(created, row => Assert.Equal(root, (string)row["watched_folder"]!));
    }

    [Fact]
    public async Task A_root_weir_cannot_see_imports_without_a_watched_folder()
    {
        using var operatorClient = await OperatorAsync();
        using var fake = FakeManager.StartDeluno(libraries: [ManagerLibrary("7", "Films-NoRoot", "movies", "/srv/elsewhere")]);
        var connectionId = await ConnectAsync(operatorClient, fake);

        var created = (await ImportAsync(operatorClient, connectionId, "7")).Elements[0]!.AsObject();

        Assert.Equal(string.Empty, (string)created["watched_folder"]!);
        Assert.Equal("7", (string)created["discovered_library_key"]!);
    }

    [Fact]
    public async Task A_duplicate_name_is_disambiguated_rather_than_refused()
    {
        using var operatorClient = await OperatorAsync();
        using var folder = new TemporaryFolder();
        var root = MakeFolder(folder, "lib");
        var manual = await operatorClient.PostWithCsrfAsync(
            LibrariesPath, new JsonObject { ["name"] = "Films", ["media_type"] = "movie" });
        LibrariesPartBChecks.Status(manual, HttpStatusCode.Created);
        using var fake = FakeManager.StartDeluno(libraries: [ManagerLibrary("7", "Films", "movies", root)]);
        var connectionId = await ConnectAsync(operatorClient, fake);

        var created = (await ImportAsync(operatorClient, connectionId, "7")).Elements[0]!.AsObject();

        Assert.Equal("Films (2)", (string)created["name"]!);
    }

    [Fact]
    public async Task Importing_no_keys_is_a_422()
    {
        using var operatorClient = await OperatorAsync();
        using var fake = FakeManager.StartDeluno();
        var connectionId = await ConnectAsync(operatorClient, fake);

        var response = await ImportAsync(operatorClient, connectionId);

        LibrariesPartBChecks.Status(response, HttpStatusCode.UnprocessableEntity);
        Assert.Equal("too_short", (string)response.Fields["detail"]![0]!["type"]!);
    }

    [Fact]
    public async Task Importing_an_unknown_key_is_a_400()
    {
        using var operatorClient = await OperatorAsync();
        using var folder = new TemporaryFolder();
        var root = MakeFolder(folder, "lib");
        using var fake = FakeManager.StartDeluno(libraries: [ManagerLibrary("7", "Films-Unknown", "movies", root)]);
        var connectionId = await ConnectAsync(operatorClient, fake);

        var response = await ImportAsync(operatorClient, connectionId, "missing");

        LibrariesPartBChecks.Status(response, HttpStatusCode.BadRequest);
        Assert.Contains(
            "no longer reports a library with id missing", (string)response.Fields["detail"]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Importing_a_refine_before_import_library_seeds_its_output_folder()
    {
        using var operatorClient = await OperatorAsync();
        using var folder = new TemporaryFolder();
        var watched = MakeFolder(folder, "watched");
        var output = MakeFolder(folder, "output");
        using var fake = FakeManager.StartDeluno(libraries:
        [
            ManagerLibrary("7", "Movies-Refine", "movies", watched, importWorkflow: "refine-before-import", processorOutputPath: output),
        ]);
        var connectionId = await ConnectAsync(operatorClient, fake);

        var created = (await ImportAsync(operatorClient, connectionId, "7")).Elements[0]!.AsObject();

        Assert.Equal(watched, (string)created["watched_folder"]!);
        Assert.Equal(output, (string)created["output_folder"]!);
    }

    // --- GET .../discover/{connection_id}/drift --------------------------------------------------------

    [Fact]
    public async Task Drift_reports_a_moved_root_and_changes_nothing()
    {
        using var operatorClient = await OperatorAsync();
        using var folder = new TemporaryFolder();
        var oldRoot = MakeFolder(folder, "old");
        var newRoot = MakeFolder(folder, "new");
        using var fake = FakeManager.StartDeluno(libraries: [ManagerLibrary("7", "Films-Moved", "movies", oldRoot)]);
        var connectionId = await ConnectAsync(operatorClient, fake);
        var libraryId = (long)(await ImportOneAsync(operatorClient, connectionId, "7"))["id"]!;
        fake.Libraries.Replace([ManagerLibrary("7", "Films-Moved", "movies", newRoot)]);

        var response = await DriftAsync(operatorClient, connectionId);

        LibrariesPartBChecks.Status(response, HttpStatusCode.OK);
        var moved = Assert.Single(response.Elements, drift => (string)drift!["kind"]! == "root_moved")!;
        Assert.Equal(newRoot, (string)moved["manager_value"]!);
        Assert.Equal(oldRoot, (string)moved["weir_value"]!);
        // Nothing applied.
        Assert.Equal(oldRoot, (string)(await LibraryAsync(operatorClient, libraryId))["watched_folder"]!);
    }

    [Fact]
    public async Task Drift_reports_a_library_the_manager_no_longer_has()
    {
        using var operatorClient = await OperatorAsync();
        using var folder = new TemporaryFolder();
        var root = MakeFolder(folder, "lib");
        using var fake = FakeManager.StartDeluno(libraries: [ManagerLibrary("7", "Gone", "movies", root)]);
        var connectionId = await ConnectAsync(operatorClient, fake);
        var libraryId = (long)(await ImportOneAsync(operatorClient, connectionId, "7"))["id"]!;
        fake.Libraries.Replace([]);

        var drift = (await DriftAsync(operatorClient, connectionId)).Elements;

        Assert.Equal(["library_removed"], drift.Select(entry => (string)entry!["kind"]!));
        Assert.Equal(libraryId, (long)drift[0]!["library_id"]!);
        Assert.Equal(root, (string)(await LibraryAsync(operatorClient, libraryId))["watched_folder"]!);
    }

    [Fact]
    public async Task Drift_reports_a_library_that_appeared()
    {
        using var operatorClient = await OperatorAsync();
        using var folder = new TemporaryFolder();
        var root = MakeFolder(folder, "lib");
        using var fake = FakeManager.StartDeluno(libraries: [ManagerLibrary("9", "New", "movies", root)]);
        var connectionId = await ConnectAsync(operatorClient, fake);

        var drift = (await DriftAsync(operatorClient, connectionId)).Elements;

        Assert.Equal(["library_added"], drift.Select(entry => (string)entry!["kind"]!));
        Assert.Equal("New", (string)drift[0]!["library_name"]!);
        var libraries = await operatorClient.GetAsync(LibrariesPath);
        Assert.All(libraries.Elements, row => Assert.NotEqual("New", (string)row!["name"]!));
    }

    [Fact]
    public async Task Drift_is_quiet_when_nothing_has_changed()
    {
        using var operatorClient = await OperatorAsync();
        using var folder = new TemporaryFolder();
        var root = MakeFolder(folder, "lib");
        using var fake = FakeManager.StartDeluno(libraries: [ManagerLibrary("7", "Films-Quiet", "movies", root)]);
        var connectionId = await ConnectAsync(operatorClient, fake);
        await ImportAsync(operatorClient, connectionId, "7");

        Assert.Empty((await DriftAsync(operatorClient, connectionId)).Elements);
    }

    // --- POST .../libraries/{library_id}/unlink --------------------------------------------------------

    [Fact]
    public async Task Unlink_keeps_the_library_but_forgets_where_it_came_from()
    {
        using var operatorClient = await OperatorAsync();
        using var folder = new TemporaryFolder();
        var root = MakeFolder(folder, "lib");
        using var fake = FakeManager.StartDeluno(libraries: [ManagerLibrary("7", "Films-Unlink", "movies", root)]);
        var connectionId = await ConnectAsync(operatorClient, fake);
        var libraryId = (long)(await ImportOneAsync(operatorClient, connectionId, "7"))["id"]!;

        var response = await operatorClient.PostWithCsrfAsync($"{LibrariesPath}/{libraryId}/unlink", new JsonObject());

        LibrariesPartBChecks.Status(response, HttpStatusCode.OK);
        var body = response.Fields;
        Assert.Equal("Films-Unlink", (string)body["name"]!);
        Assert.Equal(root, (string)body["watched_folder"]!);
        LibrariesPartBChecks.IsNull(body, "discovered_from_connection_id");
        LibrariesPartBChecks.IsNull(body, "discovered_library_key");
        LibrariesPartBChecks.IsNull(await LibraryAsync(operatorClient, libraryId), "discovered_library_key");
    }

    [Fact]
    public async Task Unlink_of_an_unknown_library_is_404()
    {
        using var operatorClient = await OperatorAsync();

        var response = await operatorClient.PostWithCsrfAsync($"{LibrariesPath}/999999/unlink", new JsonObject());

        LibrariesPartBChecks.Status(response, HttpStatusCode.NotFound);
        Assert.Equal("That workflow does not exist.", (string)response.Fields["detail"]!);
    }

    // --- openapi ------------------------------------------------------------------------------------

    [Fact]
    public async Task Discovery_operations_are_in_the_generated_openapi_schema()
    {
        using var anonymous = Server.CreateClient();

        var response = await anonymous.GetAsync("/openapi.json");

        LibrariesPartBChecks.Status(response, HttpStatusCode.OK);
        var paths = response.Fields["paths"]!.AsObject();
        foreach (var path in new[]
        {
            "/api/v1/processing/libraries/discover/{connection_id}",
            "/api/v1/processing/libraries/discover/{connection_id}/drift",
            "/api/v1/processing/libraries/discover/{connection_id}/import",
            "/api/v1/processing/libraries/{library_id}/unlink",
        })
        {
            Assert.True(paths.ContainsKey(path), path);
        }
    }

    /// <summary>An admin session on the class's server, with only the seeded libraries and no connections left.</summary>
    private async Task<WeirClient> OperatorAsync()
    {
        var admin = await Server.CreateAdminClientAsync();
        try
        {
            await ResetAsync(admin);
        }
        catch
        {
            admin.Dispose();
            throw;
        }

        return admin;
    }

    private static string MakeFolder(TemporaryFolder parent, string name) => Directory.CreateDirectory(Path.Combine(parent.Path, name)).FullName;
}
