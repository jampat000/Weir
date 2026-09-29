using System.Net;
using System.Text.Json.Nodes;

namespace Weir.Api.Tests.Platform;

/// <summary>
/// A library's minimum size and wait over real HTTP: left unset it follows Settings › Performance and reports what that comes
/// to, and a value it sets itself is kept when Performance changes.
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

    private static async Task<JsonNode> CreateLibraryAsync(ApiTestClient client, string name, long? minFileSizeMb = null, long? minFileAgeSeconds = null)
    {
        var body = new JsonObject
        {
            ["csrf_token"] = await client.CsrfAsync(),
            ["name"] = name,
            ["media_type"] = "movie",
            ["watched_folder"] = @$"c:\{name}-in",
            ["output_folder"] = @$"c:\{name}-out",
        };
        if (minFileSizeMb is { } size)
        {
            body["min_file_size_mb"] = size;
        }

        if (minFileAgeSeconds is { } wait)
        {
            body["min_file_age_seconds"] = wait;
        }

        using var created = await client.PostAsync(Libraries, body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return await ApiTestClient.Json(created);
    }

    private static async Task SetPerformanceAsync(ApiTestClient client, long minFileAgeSeconds, long minInputFileSizeMb)
    {
        using var saved = await client.PutAsync(
            OperatorSettings,
            new { csrf_token = await client.CsrfAsync(), min_file_age_seconds = minFileAgeSeconds, min_input_file_size_mb = minInputFileSizeMb });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private static async Task<JsonNode> LibraryAsync(ApiTestClient client, long id)
    {
        using var response = await client.GetAsync($"{Libraries}/{id}");
        return await ApiTestClient.Json(response);
    }

    [Fact]
    public async Task A_library_created_without_a_minimum_size_or_wait_follows_Performance()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;
        await SetPerformanceAsync(client, minFileAgeSeconds: 10, minInputFileSizeMb: 1);

        var library = await CreateLibraryAsync(client, "anime");

        Assert.Null(library["min_file_size_mb"]);
        Assert.Null(library["min_file_age_seconds"]);
        Assert.Equal(1, library["effective_min_file_size_mb"]!.GetValue<long>());
        Assert.Equal(10, library["effective_min_file_age_seconds"]!.GetValue<long>());
    }

    [Fact]
    public async Task Changing_Performance_changes_what_a_library_that_follows_it_reports()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;
        var library = await CreateLibraryAsync(client, "anime");

        await SetPerformanceAsync(client, minFileAgeSeconds: 120, minInputFileSizeMb: 200);

        var after = await LibraryAsync(client, library["id"]!.GetValue<long>());
        Assert.Equal(200, after["effective_min_file_size_mb"]!.GetValue<long>());
        Assert.Equal(120, after["effective_min_file_age_seconds"]!.GetValue<long>());
    }

    [Fact]
    public async Task A_library_that_sets_its_own_values_keeps_them_when_Performance_changes()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;
        var library = await CreateLibraryAsync(client, "kids", minFileSizeMb: 5, minFileAgeSeconds: 0);

        await SetPerformanceAsync(client, minFileAgeSeconds: 120, minInputFileSizeMb: 200);

        var after = await LibraryAsync(client, library["id"]!.GetValue<long>());
        Assert.Equal(5, after["min_file_size_mb"]!.GetValue<long>());
        Assert.Equal(5, after["effective_min_file_size_mb"]!.GetValue<long>());
        Assert.Equal(0, after["effective_min_file_age_seconds"]!.GetValue<long>());
    }

    [Fact]
    public async Task Saving_null_hands_a_library_back_to_Performance()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;
        var library = await CreateLibraryAsync(client, "kids", minFileSizeMb: 5, minFileAgeSeconds: 0);
        await SetPerformanceAsync(client, minFileAgeSeconds: 120, minInputFileSizeMb: 200);

        using var saved = await client.PutAsync(
            $"{Libraries}/{library["id"]!.GetValue<long>()}",
            new
            {
                csrf_token = await client.CsrfAsync(),
                name = "kids",
                media_type = "movie",
                watched_folder = @"c:\kids-in",
                output_folder = @"c:\kids-out",
                min_file_size_mb = (long?)null,
                min_file_age_seconds = (long?)null,
            });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var after = await ApiTestClient.Json(saved);
        Assert.Null(after["min_file_size_mb"]);
        Assert.Equal(200, after["effective_min_file_size_mb"]!.GetValue<long>());
        Assert.Equal(120, after["effective_min_file_age_seconds"]!.GetValue<long>());
    }

    [Fact]
    public async Task The_seeded_libraries_follow_Performance_on_a_new_install()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;
        await SetPerformanceAsync(client, minFileAgeSeconds: 10, minInputFileSizeMb: 1);

        using var listed = await client.GetAsync(Libraries);

        foreach (var library in (await ApiTestClient.Json(listed)).AsArray())
        {
            Assert.Null(library!["min_file_size_mb"]);
            Assert.Equal(10, library["effective_min_file_age_seconds"]!.GetValue<long>());
        }
    }
}
