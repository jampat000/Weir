using System.Net;
using System.Text.Json.Nodes;

namespace Weir.Api.Tests.Platform;

/// <summary>
/// A workflow's wait and minimum size over real HTTP: each is the workflow's own value, a new workflow starts at 60 seconds and
/// 50 MB, and Setup › Performance › Speed holds neither.
/// </summary>
public sealed class LibraryIntakeLimitsApiTests
{
    private const string Libraries = "/api/v1/processing/libraries";
    private const string OperatorSettings = "/api/v1/processing/operator-settings";

    private static async Task<(WeirTestServer Server, ApiTestClient Client)> SignedInAsync()
    {
        var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        return (server, client);
    }

    private static async Task<JsonNode> CreateLibraryAsync(ApiTestClient client, string name, JsonObject? extra = null)
    {
        var body = new JsonObject
        {
            ["csrf_token"] = await client.CsrfAsync(),
            ["name"] = name,
            ["media_type"] = "movie",
            ["watched_folder"] = @$"c:\{name}-in",
            ["output_folder"] = @$"c:\{name}-out",
        };
        foreach (var (key, value) in extra ?? [])
        {
            body[key] = value?.DeepClone();
        }

        using var created = await client.PostAsync(Libraries, body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return await ApiTestClient.Json(created);
    }

    private static async Task<JsonNode> LibraryAsync(ApiTestClient client, long id)
    {
        using var response = await client.GetAsync($"{Libraries}/{id}");
        return await ApiTestClient.Json(response);
    }

    [Fact]
    public async Task A_workflow_created_without_a_wait_or_minimum_size_starts_at_sixty_seconds_and_fifty_megabytes()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        var library = await CreateLibraryAsync(client, "anime");

        Assert.Equal(60, library["ready_after_seconds"]!.GetValue<long>());
        Assert.Equal(50, library["min_file_size_mb"]!.GetValue<long>());
    }

    [Fact]
    public async Task A_workflow_reports_no_inherited_or_retired_wait_fields()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        var library = (await CreateLibraryAsync(client, "anime")).AsObject();

        foreach (var removed in new[] { "effective_min_file_size_mb", "effective_min_file_age_seconds", "min_file_age_seconds", "hold_minutes", "file_detection_interval_seconds" })
        {
            Assert.False(library.ContainsKey(removed), removed);
        }
    }

    [Fact]
    public async Task A_workflow_keeps_the_wait_and_minimum_size_it_was_given()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        var created = await CreateLibraryAsync(client, "kids", new JsonObject { ["ready_after_seconds"] = 0, ["min_file_size_mb"] = 5 });

        var stored = await LibraryAsync(client, created["id"]!.GetValue<long>());
        Assert.Equal(0, stored["ready_after_seconds"]!.GetValue<long>());
        Assert.Equal(5, stored["min_file_size_mb"]!.GetValue<long>());
    }

    [Fact]
    public async Task A_wait_longer_than_a_workflow_can_hold_is_refused()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        using var refused = await client.PostAsync(
            Libraries,
            new
            {
                csrf_token = await client.CsrfAsync(),
                name = "slow",
                media_type = "movie",
                watched_folder = @"c:\slow-in",
                output_folder = @"c:\slow-out",
                ready_after_seconds = 1_209_601,
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
    }

    [Fact]
    public async Task An_older_client_that_still_sends_the_three_waits_is_answered_and_they_are_ignored()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        var created = await CreateLibraryAsync(
            client,
            "older",
            new JsonObject { ["min_file_age_seconds"] = 5, ["hold_minutes"] = 10, ["file_detection_interval_seconds"] = 999 });

        Assert.Equal(60, created["ready_after_seconds"]!.GetValue<long>());
    }

    [Fact]
    public async Task Changing_Performance_changes_nothing_about_a_workflow()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;
        var library = await CreateLibraryAsync(client, "anime");

        using var saved = await client.PutAsync(
            OperatorSettings, new { csrf_token = await client.CsrfAsync(), min_file_age_seconds = 120, min_input_file_size_mb = 200 });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var after = await LibraryAsync(client, library["id"]!.GetValue<long>());
        Assert.Equal(60, after["ready_after_seconds"]!.GetValue<long>());
        Assert.Equal(50, after["min_file_size_mb"]!.GetValue<long>());
    }

    [Fact]
    public async Task The_seeded_workflows_hold_a_wait_and_minimum_size_of_their_own()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        using var listed = await client.GetAsync(Libraries);

        foreach (var library in (await ApiTestClient.Json(listed)).AsArray())
        {
            Assert.Equal(60, library!["ready_after_seconds"]!.GetValue<long>());
            Assert.Equal(50, library["min_file_size_mb"]!.GetValue<long>());
        }
    }
}
