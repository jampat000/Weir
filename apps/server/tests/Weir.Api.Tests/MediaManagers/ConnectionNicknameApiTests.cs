using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Weir.Api.Tests.Platform;
using Weir.Infrastructure.MediaManagers;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.MediaManagers;

/// <summary>
/// The optional nickname on a media manager or download client connection: set and cleared through the API, kept apart
/// from the name Weir derives, and shown after that name wherever the connection is written in a sentence.
/// </summary>
public sealed class ConnectionNicknameApiTests
{
    private const string ManagerConnections = "/api/v1/media-managers/connections";
    private const string ClientConnections = "/api/v1/download-clients/connections";

    private static async Task<(WeirTestServer Server, ApiTestClient Client)> StartAsync()
    {
        var server = await WeirTestServer.StartAsync(
            [("WEIR_SESSION_SECRET", Secret), ("WEIR_PROCESSING_WORKER_COUNT", "0"), ("WEIR_MEDIA_MANAGER_WEBHOOK_SECRET", "")],
            configureServices: services => services.AddSingleton<IManagerHttpHandlerFactory>(new ScriptedManager()));
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        return (server, client);
    }

    private static async Task<JsonNode> SendAsync(ApiTestClient client, HttpMethod method, string path, Dictionary<string, object?> body)
    {
        body["csrf_token"] = await client.CsrfAsync();
        using var response = await client.SendAsync(method, path, body);
        Assert.True(response.IsSuccessStatusCode, $"{method} {path} answered {(int)response.StatusCode}");
        return await Json(response);
    }

    private static Task<JsonNode> CreateManagerAsync(ApiTestClient client, string? nickname = null) =>
        SendAsync(client, HttpMethod.Post, ManagerConnections, new()
        {
            ["kind"] = "deluno",
            ["base_url"] = "http://192.0.2.10:5099",
            ["api_key"] = "deluno_secret_key",
            ["nickname"] = nickname,
        });

    private static Task<JsonNode> CreateClientAsync(ApiTestClient client, string? nickname = null) =>
        SendAsync(client, HttpMethod.Post, ClientConnections, new()
        {
            ["kind"] = "sabnzbd",
            ["base_url"] = "http://192.0.2.30:8080",
            ["api_key"] = "key",
            ["nickname"] = nickname,
        });

    private static Task<JsonNode> EditAsync(ApiTestClient client, string collection, Dictionary<string, object?> changes) =>
        SendAsync(client, HttpMethod.Put, $"{collection}/1", changes);

    private static string? Nickname(JsonNode connection) => connection["nickname"]?.GetValue<string>();

    [Fact]
    public async Task A_media_manager_can_be_added_with_a_nickname_and_keeps_its_derived_name()
    {
        var (server, client) = await StartAsync();
        await using var _server = server;

        var created = await CreateManagerAsync(client, "4K");

        Assert.Equal(("Deluno on 192.0.2.10", "4K"), (created["name"]!.GetValue<string>(), Nickname(created)));
    }

    [Fact]
    public async Task A_media_manager_added_without_a_nickname_has_none()
    {
        var (server, client) = await StartAsync();
        await using var _server = server;

        var created = await CreateManagerAsync(client);

        Assert.Null(Nickname(created));
    }

    [Fact]
    public async Task A_nickname_is_trimmed_and_a_blank_one_means_none()
    {
        var (server, client) = await StartAsync();
        await using var _server = server;
        var trimmed = await CreateManagerAsync(client, "  Kids  ");
        Assert.Equal("Kids", Nickname(trimmed));

        var blanked = await EditAsync(client, ManagerConnections, new() { ["nickname"] = "   " });

        Assert.Null(Nickname(blanked));
    }

    [Fact]
    public async Task Editing_a_media_manager_without_a_nickname_leaves_its_nickname_alone()
    {
        var (server, client) = await StartAsync();
        await using var _server = server;
        await CreateManagerAsync(client, "4K");

        var edited = await EditAsync(client, ManagerConnections, new() { ["enabled"] = false });

        Assert.Equal("4K", Nickname(edited));
    }

    [Fact]
    public async Task A_nickname_can_be_changed_after_the_media_manager_is_added()
    {
        var (server, client) = await StartAsync();
        await using var _server = server;
        await CreateManagerAsync(client, "4K");

        var edited = await EditAsync(client, ManagerConnections, new() { ["nickname"] = "Kids" });

        Assert.Equal("Kids", Nickname(edited));
    }

    [Fact]
    public async Task A_nickname_of_thirty_characters_is_accepted_and_one_more_is_refused()
    {
        var (server, client) = await StartAsync();
        await using var _server = server;
        var accepted = await CreateManagerAsync(client, new string('a', 30));
        Assert.Equal(30, Nickname(accepted)!.Length);

        using var response = await client.PutAsync(
            $"{ManagerConnections}/1", new { csrf_token = await client.CsrfAsync(), nickname = new string('a', 31) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task The_name_a_media_manager_sends_when_it_connects_is_never_taken_as_the_nickname()
    {
        var (server, client) = await StartAsync();
        await using var _server = server;

        var created = await SendAsync(client, HttpMethod.Post, ManagerConnections, new()
        {
            ["kind"] = "deluno",
            ["name"] = "Deluno",
            ["base_url"] = "http://192.0.2.10:5099",
            ["api_key"] = "deluno_secret_key",
        });

        Assert.Null(Nickname(created));
    }

    [Fact]
    public async Task The_nickname_follows_the_name_in_the_warning_about_a_missing_webhook_secret()
    {
        var (server, client) = await StartAsync();
        await using var _server = server;

        var created = await CreateManagerAsync(client, "4K");

        Assert.EndsWith(
            "add it to Deluno on 192.0.2.10 · 4K.",
            created["unsigned_webhook_warning"]!.GetValue<string>(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_connection_test_names_the_media_manager_with_its_nickname()
    {
        var (server, client) = await StartAsync();
        await using var _server = server;
        await SendAsync(client, HttpMethod.Post, ManagerConnections, new()
        {
            ["kind"] = "deluno",
            ["base_url"] = "http://127.0.0.1:1",
            ["api_key"] = "deluno_secret_key",
            ["nickname"] = "4K",
        });

        var tested = await SendAsync(client, HttpMethod.Post, $"{ManagerConnections}/1/test", []);

        Assert.StartsWith("Weir could not reach Deluno on 127.0.0.1 · 4K at http://127.0.0.1:1.", tested["detail"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_download_client_can_be_added_with_a_nickname_and_keeps_its_derived_name()
    {
        var (server, client) = await StartAsync();
        await using var _server = server;

        var created = await CreateClientAsync(client, "Usenet");

        Assert.Equal(("SABnzbd on 192.0.2.30", "Usenet"), (created["name"]!.GetValue<string>(), Nickname(created)));
    }

    [Fact]
    public async Task A_download_clients_nickname_can_be_changed_kept_and_cleared()
    {
        var (server, client) = await StartAsync();
        await using var _server = server;
        await CreateClientAsync(client, "Usenet");

        var changed = await EditAsync(client, ClientConnections, new() { ["nickname"] = "News" });
        var kept = await EditAsync(client, ClientConnections, new() { ["enabled"] = false });
        var cleared = await EditAsync(client, ClientConnections, new() { ["nickname"] = "" });

        Assert.Equal(("News", "News", null), (Nickname(changed), Nickname(kept), Nickname(cleared)));
    }

    [Fact]
    public async Task A_download_clients_nickname_is_limited_to_thirty_characters()
    {
        var (server, client) = await StartAsync();
        await using var _server = server;
        await CreateClientAsync(client);

        using var response = await client.PutAsync(
            $"{ClientConnections}/1", new { csrf_token = await client.CsrfAsync(), nickname = new string('a', 31) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task The_name_a_download_client_sends_is_never_taken_as_the_nickname()
    {
        var (server, client) = await StartAsync();
        await using var _server = server;

        var created = await SendAsync(client, HttpMethod.Post, ClientConnections, new()
        {
            ["kind"] = "sabnzbd",
            ["name"] = "Usenet",
            ["base_url"] = "http://192.0.2.30:8080",
            ["api_key"] = "key",
        });

        Assert.Null(Nickname(created));
    }
}
