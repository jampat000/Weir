using System.Net;

namespace Weir.Api.Tests.Platform;

/// <summary>
/// Settings › Cleanup over real HTTP: each cleanup job's interval is a setting, and <c>GET /processing/maintenance</c>
/// says how often each runs and, while it is switched on, when it next does.
/// </summary>
public sealed class CleanupScheduleApiTests
{
    private static async Task<(WeirTestServer Server, ApiTestClient Client)> SignedInAsync()
    {
        var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        return (server, client);
    }

    [Fact]
    public async Task An_interval_saves_between_fifteen_minutes_and_thirty_days()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        using var saved = await client.PutAsync(
            "/api/v1/processing/operator-settings",
            new { csrf_token = await client.CsrfAsync(), work_temp_stale_sweep_interval_seconds = 3600, failure_cleanup_interval_seconds = 86400 });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var body = await ApiTestClient.Json(saved);
        Assert.Equal(3600, body["work_temp_stale_sweep_interval_seconds"]!.GetValue<long>());
        Assert.Equal(86400, body["failure_cleanup_interval_seconds"]!.GetValue<long>());

        using var tooOften = await client.PutAsync(
            "/api/v1/processing/operator-settings",
            new { csrf_token = await client.CsrfAsync(), work_temp_stale_sweep_interval_seconds = 60 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooOften.StatusCode);
    }

    [Fact]
    public async Task Maintenance_says_how_often_each_job_runs_and_has_no_next_run_while_it_is_off()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;
        using var saved = await client.PutAsync(
            "/api/v1/processing/operator-settings",
            new { csrf_token = await client.CsrfAsync(), failure_cleanup_interval_seconds = 7200 });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var state = await client.GetAsync("/api/v1/processing/maintenance");
        var families = (await ApiTestClient.Json(state))["families"]!.AsArray();
        var cleanup = families.Single(f => f!["family"]!.GetValue<string>() == "failure_cleanup")!;

        Assert.False(cleanup["enabled"]!.GetValue<bool>());
        Assert.Equal(7200, cleanup["interval_seconds"]!.GetValue<long>());
        Assert.Null(cleanup["next_run_at"]);
    }
}
