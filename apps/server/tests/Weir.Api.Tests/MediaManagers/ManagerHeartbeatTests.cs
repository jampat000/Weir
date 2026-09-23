using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Weir.Api.Tests.Platform;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Scheduling;

namespace Weir.Api.Tests.MediaManagers;

/// <summary>
/// The media manager heartbeat (23 Sep 2026): each run saves every enabled manager's connection test, so a manager
/// going quiet, or coming back, shows without anyone pressing Test.
/// </summary>
public sealed class ManagerHeartbeatTests
{
    [Fact]
    public async Task A_run_saves_whether_each_manager_answered_and_why()
    {
        var manager = new ScriptedManager();
        await using var server = await WeirTestServer.StartAsync(
            [("WEIR_SESSION_SECRET", ApiTestClient.Secret), ("WEIR_PROCESSING_WORKER_COUNT", "0")],
            configureServices: services => services.AddSingleton<IManagerHttpHandlerFactory>(manager));
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();

        using var created = await client.PostAsync(
            "/api/v1/media-managers/connections",
            new { csrf_token = await client.CsrfAsync(), kind = "sonarr", name = "Sonarr", base_url = "http://192.0.2.10:8989", api_key = "k" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ApiTestClient.Json(created))["id"]!.GetValue<long>();
        var heartbeat = server.Services.GetServices<IPeriodicTask>().OfType<ManagerHeartbeatTask>().Single();

        // Nothing answers yet: the manager is unreachable, and the detail says so in words.
        await heartbeat.RunOnceAsync(CancellationToken.None);
        var quiet = await ConnectionAsync(client, id);
        Assert.False(quiet["last_test_ok"]!.GetValue<bool>());
        Assert.Contains("could not reach Sonarr", quiet["last_test_detail"]!.GetValue<string>(), StringComparison.Ordinal);

        // It comes back, and the next beat says so.
        manager.Json(HttpMethod.Get, "/api/v3/system/status", "{\"version\": \"4.0.9\"}");
        await heartbeat.RunOnceAsync(CancellationToken.None);
        var back = await ConnectionAsync(client, id);
        Assert.True(back["last_test_ok"]!.GetValue<bool>());
        Assert.Equal("Connected. Weir can reach Sonarr.", back["last_test_detail"]!.GetValue<string>());
    }

    private static async Task<System.Text.Json.Nodes.JsonNode> ConnectionAsync(ApiTestClient client, long id)
    {
        using var response = await client.GetAsync($"/api/v1/media-managers/connections/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestClient.Json(response);
    }
}
