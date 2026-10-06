using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Libraries.LibrariesPartAHelpers;
using static Weir.Contract.Tests.Libraries.LibraryRequests;

namespace Weir.Contract.Tests.Libraries;

/// <summary>Libraries and rule sets: create, edit, delete, reorder, and the reject-support checks.</summary>
[ContractArea("libraries")]
public sealed partial class ProcessingLibrariesApiTests(ServerFixture fixture) : IClassFixture<ServerFixture>
{
    // Disabled: the server's own periodic scan queues a scan job for every enabled library with a
    // watched folder, and that job counts as queued work. Nothing asserted here depends on `enabled`.
    private static Task<WeirResponse> Create(WeirClient client, params (string Name, JsonNode? Value)[] overrides) =>
        CreateAsync(
            client,
            Obj(
                ("enabled", false),
                ("name", "Movies 4K"),
                ("media_type", "movie"),
                ("watched_folder", "/srv/4k/in"),
                ("output_folder", "/srv/4k/out"),
                ("media_extensions_csv", ".mkv,.mp4")),
            overrides);

    /// <summary>The admin, with only the seeded libraries (and no unused rule sets) left, so each test starts the same way.</summary>
    private async Task<WeirClient> OperatorAsync()
    {
        var admin = await fixture.Server.CreateAdminClientAsync();
        try
        {
            await RemoveAddedLibrariesAsync(admin);
            foreach (var row in (await admin.GetAsync(RuleSets)).Elements)
            {
                if ((int)row!["used_by_library_count"]! == 0)
                {
                    await admin.DeleteWithCsrfBodyAsync($"{RuleSets}/{(long)row["id"]!}");
                }
            }
        }
        catch
        {
            admin.Dispose();
            throw;
        }

        return admin;
    }

    [Fact]
    public async Task Libraries_require_authentication()
    {
        using var client = fixture.Server.CreateClient();

        (await client.GetAsync(LibrariesUrl)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_seeded_libraries_are_listed()
    {
        using var operatorClient = await OperatorAsync();

        var byName = (await operatorClient.GetAsync(LibrariesUrl)).Elements.ToDictionary(row => (string)row!["name"]!, row => row!);
        Assert.Contains("Movies", byName.Keys);
        Assert.Contains("TV", byName.Keys);
        Assert.Equal("movie", (string)byName["Movies"]["media_type"]!);
        Assert.Equal("tv", (string)byName["TV"]["media_type"]!);
        // The former module constants arrive as this library's saved data.
        Assert.Contains(".mkv", (string)byName["Movies"]["media_extensions_csv"]!);
    }

    [Fact]
    public async Task A_third_library_can_be_added_and_edited()
    {
        using var operatorClient = await OperatorAsync();
        var created = await Create(operatorClient);
        created.ShouldBe(HttpStatusCode.Created);
        var row = created.Fields;
        Assert.Equal("Movies 4K", (string)row["name"]!);
        Assert.Equal("movie", (string)row["media_type"]!);

        var updated = await operatorClient.PutWithCsrfAsync(
            $"{LibrariesUrl}/{(long)row["id"]!}",
            Obj(
                ("enabled", false),
                ("name", "Movies 4K"),
                ("media_type", "movie"),
                ("watched_folder", "/srv/4k/in2"),
                ("output_folder", "/srv/4k/out"),
                ("min_file_size_mb", 900),
                ("created_after", "2026-01-01T00:00:00Z"),
                ("created_before", "2027-01-01T00:00:00Z"),
                ("modified_after", "2026-02-01T00:00:00Z"),
                ("modified_before", "2026-12-01T00:00:00Z"),
                ("top_level_only", true)));
        updated.ShouldBe(HttpStatusCode.OK);
        Assert.Equal("/srv/4k/in2", (string)updated.Fields["watched_folder"]!);
        Assert.Equal(900, (int)updated.Fields["min_file_size_mb"]!);
        Assert.StartsWith("2026-01-01T00:00:00", (string)updated.Fields["created_after"]!, StringComparison.Ordinal);
        Assert.StartsWith("2027-01-01T00:00:00", (string)updated.Fields["created_before"]!, StringComparison.Ordinal);
        Assert.StartsWith("2026-02-01T00:00:00", (string)updated.Fields["modified_after"]!, StringComparison.Ordinal);
        Assert.StartsWith("2026-12-01T00:00:00", (string)updated.Fields["modified_before"]!, StringComparison.Ordinal);
        Assert.True((bool)updated.Fields["top_level_only"]!);
        var fetched = await operatorClient.GetAsync($"{LibrariesUrl}/{(long)row["id"]!}");
        fetched.ShouldBe(HttpStatusCode.OK);
        Assert.Equal("/srv/4k/in2", (string)fetched.Fields["watched_folder"]!);
    }

    [Fact]
    public async Task Detection_windows_must_be_ordered_and_timezone_aware()
    {
        using var operatorClient = await OperatorAsync();

        var reversedWindow = await Create(
            operatorClient, ("created_after", "2027-01-01T00:00:00Z"), ("created_before", "2026-01-01T00:00:00Z"));
        reversedWindow.ShouldBe(HttpStatusCode.UnprocessableEntity);
        Assert.Contains("Created after must be earlier", reversedWindow.Text);

        var timezoneMissing = await Create(operatorClient, ("modified_after", "2026-01-01T00:00:00"));
        timezoneMissing.ShouldBe(HttpStatusCode.UnprocessableEntity);
        Assert.Contains("must include a timezone", timezoneMissing.Text);
    }

    [Fact]
    public async Task A_duplicate_name_is_refused()
    {
        using var operatorClient = await OperatorAsync();

        (await Create(operatorClient)).ShouldBe(HttpStatusCode.Created);
        var again = await Create(operatorClient);
        again.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Contains("already exists", (string)again.Fields["detail"]!);
    }

    [Fact]
    public async Task An_unknown_media_type_is_refused()
    {
        using var operatorClient = await OperatorAsync();

        (await Create(operatorClient, ("media_type", "anime"))).ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_relative_watched_folder_is_refused()
    {
        using var operatorClient = await OperatorAsync();

        var response = await Create(operatorClient, ("watched_folder", "relative/path"));

        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Contains("absolute", (string)response.Fields["detail"]!);
    }

    [Fact]
    public async Task A_parent_segment_in_a_folder_is_refused()
    {
        using var operatorClient = await OperatorAsync();

        var response = await Create(operatorClient, ("watched_folder", "/srv/4k/../escape"));

        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Contains("..", (string)response.Fields["detail"]!);
    }

    [Fact]
    public async Task The_filesystem_root_cannot_be_a_watched_folder()
    {
        using var operatorClient = await OperatorAsync();

        var response = await Create(operatorClient, ("watched_folder", "/"), ("output_folder", "/srv/4k/out"));

        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Contains("root of a drive", (string)response.Fields["detail"]!);
    }

    [Fact]
    public async Task A_linux_system_folder_cannot_be_a_watched_folder()
    {
        using var operatorClient = await OperatorAsync();

        var response = await Create(operatorClient, ("watched_folder", "/etc"), ("output_folder", "/srv/4k/out"));

        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Contains("system folder", (string)response.Fields["detail"]!);
    }

    [Fact]
    public async Task Linking_a_manager_connection_that_does_not_exist_is_refused()
    {
        using var operatorClient = await OperatorAsync();

        var response = await Create(operatorClient, ("manager_connection_ids", new JsonArray(4242)));

        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Contains("media manager connection", ((string)response.Fields["detail"]!).ToLowerInvariant());
    }

    [Fact]
    public async Task A_library_can_be_deleted_when_nothing_is_queued()
    {
        using var operatorClient = await OperatorAsync();
        var row = (await Create(operatorClient)).Fields;

        (await operatorClient.DeleteWithCsrfBodyAsync($"{LibrariesUrl}/{(long)row["id"]!}")).ShouldBe(HttpStatusCode.NoContent);

        Assert.All((await operatorClient.GetAsync(LibrariesUrl)).Elements, other => Assert.NotEqual((long)row["id"]!, (long)other!["id"]!));
    }

    [Fact]
    public async Task Deleting_a_library_with_queued_work_is_refused()
    {
        // Those jobs resolve their folders from this library, and Processing deletes folders.
        await using var server = await WeirServer.StartNewAsync();
        var row = await CreateOnFreshServerAsync(server);
        await SeedLibraryJobAsync(server, "processing.file.remux_pass.v1:library-guard", "pending", (long)row["id"]!);

        using var admin = await server.CreateAdminClientAsync();
        var response = await admin.DeleteWithCsrfBodyAsync($"{LibrariesUrl}/{(long)row["id"]!}");
        response.ShouldBe(HttpStatusCode.Conflict);
        Assert.Contains("queued or running", (string)response.Fields["detail"]!);
        Assert.Contains((await admin.GetAsync(LibrariesUrl)).Elements, other => (long)other!["id"]! == (long)row["id"]!);
    }

    [Fact]
    public async Task The_active_job_count_is_reported_so_the_screen_can_explain_the_refusal()
    {
        await using var server = await WeirServer.StartNewAsync();
        var row = await CreateOnFreshServerAsync(server);
        Assert.Equal(0, (int)row["active_job_count"]!);

        await SeedLibraryJobAsync(server, "processing.file.remux_pass.v1:count", "pending", (long)row["id"]!);

        using var admin = await server.CreateAdminClientAsync();
        var again = await admin.GetAsync($"{LibrariesUrl}/{(long)row["id"]!}");
        Assert.Equal(1, (int)again.Fields["active_job_count"]!);
    }

    [Fact]
    public async Task A_completed_job_does_not_block_deletion()
    {
        await using var server = await WeirServer.StartNewAsync();
        var row = await CreateOnFreshServerAsync(server);
        await SeedLibraryJobAsync(server, "processing.file.remux_pass.v1:done", "completed", (long)row["id"]!);

        using var admin = await server.CreateAdminClientAsync();
        (await admin.DeleteWithCsrfBodyAsync($"{LibrariesUrl}/{(long)row["id"]!}")).ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Reordering_decides_which_library_a_scope_only_payload_resolves_to()
    {
        using var operatorClient = await OperatorAsync();
        var created = (await Create(operatorClient)).Fields;
        var ids = (await operatorClient.GetAsync(LibrariesUrl)).Elements.Select(row => (long)row!["id"]!).ToList();
        var createdId = (long)created["id"]!;
        var reordered = new[] { createdId }.Concat(ids.Where(id => id != createdId)).ToList();

        var response = await operatorClient.PostWithCsrfAsync($"{LibrariesUrl}/reorder", Obj(("library_ids_in_order", Ids(reordered))));
        response.ShouldBe(HttpStatusCode.OK);
        Assert.Equal(reordered, response.Elements.Select(row => (long)row!["id"]!));
        Assert.Equal(reordered, (await operatorClient.GetAsync(LibrariesUrl)).Elements.Select(row => (long)row!["id"]!));

        // Put the seeded order back for the rest of the module.
        var restored = ids.Where(id => id != createdId).Concat([createdId]).ToList();
        (await operatorClient.PostWithCsrfAsync($"{LibrariesUrl}/reorder", Obj(("library_ids_in_order", Ids(restored)))))
            .ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reordering_must_list_every_library()
    {
        using var operatorClient = await OperatorAsync();
        await Create(operatorClient);

        var response = await operatorClient.PostWithCsrfAsync($"{LibrariesUrl}/reorder", Obj(("library_ids_in_order", Ids([1]))));

        response.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_rule_set_in_use_cannot_be_deleted()
    {
        // ADR-0014 section 3 makes rule sets shared on purpose; a cascade would strip handling.
        using var operatorClient = await OperatorAsync();
        var made = await operatorClient.PostWithCsrfAsync(RuleSets, Obj(("name", "Anime"), ("primary_audio_lang", "jpn")));
        made.ShouldBe(HttpStatusCode.Created);
        var ruleSet = made.Fields;

        (await Create(operatorClient, ("name", "Anime Movies"), ("rule_set_id", (long)ruleSet["id"]!))).ShouldBe(HttpStatusCode.Created);

        var response = await operatorClient.DeleteWithCsrfBodyAsync($"{RuleSets}/{(long)ruleSet["id"]!}");
        response.ShouldBe(HttpStatusCode.Conflict);
        Assert.Contains("still used by", (string)response.Fields["detail"]!);

        var listed = (await operatorClient.GetAsync(RuleSets)).Elements.ToDictionary(row => (long)row!["id"]!, row => row!);
        Assert.Equal(1, (int)listed[(long)ruleSet["id"]!]["used_by_library_count"]!);
    }

    [Fact]
    public async Task Libraries_and_rule_sets_reach_the_generated_openapi_schema()
    {
        using var client = fixture.Server.CreateClient();
        var response = await client.GetAsync("/openapi.json");
        response.ShouldBe(HttpStatusCode.OK);
        var schema = response.Fields;
        foreach (var path in new[]
        {
            "/api/v1/processing/libraries",
            "/api/v1/processing/libraries/{library_id}",
            "/api/v1/processing/libraries/reorder",
            "/api/v1/processing/rule-sets",
        })
        {
            Assert.True(schema["paths"]!.AsObject().ContainsKey(path), path);
        }

        var properties = schema["components"]!["schemas"]!["ProcessingLibraryOut"]!["properties"]!.AsObject();
        foreach (var name in new[] { "media_extensions_csv", "manager_connection_ids", "active_job_count" })
        {
            Assert.True(properties.ContainsKey(name), name);
        }
    }

    // --- the reject failure policy (#471) ------------------------------------------------------------

    [Fact]
    public async Task Reject_cannot_be_saved_for_a_library_no_manager_can_take_one_for()
    {
        // Reject deletes downloads, so it is refused rather than saved and silently never used.
        using var operatorClient = await OperatorAsync();

        var response = await Create(operatorClient, ("failure_policy", "reject"));

        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Contains("cannot use Reject yet", (string)response.Fields["detail"]!);
        Assert.Contains("Link a media manager", (string)response.Fields["detail"]!);
        var names = (await operatorClient.GetAsync(LibrariesUrl)).Elements.Select(row => (string)row!["name"]!).ToHashSet();
        Assert.DoesNotContain("Movies 4K", names);
    }

    [Fact]
    public async Task Reject_support_explains_itself()
    {
        using var operatorClient = await OperatorAsync();

        var response = await operatorClient.GetAsync($"{Api}/processing/reject-support");

        response.ShouldBe(HttpStatusCode.OK);
        Assert.False((bool)response.Fields["available"]!);
        Assert.Contains("Link a media manager", (string)response.Fields["reason"]!);
    }

    [Fact]
    public async Task Reject_support_needs_a_session()
    {
        using var client = fixture.Server.CreateClient();

        (await client.GetAsync($"{Api}/processing/reject-support")).ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static JsonArray Ids(IEnumerable<long> ids) => new(ids.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());

    /// <summary>A new library on a server of its own, made through a signed-in client that is then closed.</summary>
    private static async Task<JsonObject> CreateOnFreshServerAsync(WeirServer server)
    {
        using var admin = await server.CreateAdminClientAsync();
        return (await Create(admin)).Fields;
    }

    /// <summary>Adds a job row for the library while the server is stopped; the server restarts on a new port.</summary>
    private static async Task SeedLibraryJobAsync(WeirServer server, string dedupeKey, string status, long libraryId)
    {
        await using var database = await server.StopForDatabaseAsync();
        InsertJob(
            database.Connection,
            dedupeKey,
            status: status,
            payload: new JsonObject { ["relative_media_path"] = "a.mkv", ["library_id"] = libraryId });
    }
}
