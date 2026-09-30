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
            new { csrf_token = await client.CsrfAsync(), work_temp_stale_sweep_interval_seconds = 3600, unclaimed_handback_cleanup_interval_seconds = 86400 });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var body = await ApiTestClient.Json(saved);
        Assert.Equal(3600, body["work_temp_stale_sweep_interval_seconds"]!.GetValue<long>());
        Assert.Equal(86400, body["unclaimed_handback_cleanup_interval_seconds"]!.GetValue<long>());

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
            new { csrf_token = await client.CsrfAsync(), unclaimed_handback_cleanup_interval_seconds = 7200 });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var state = await client.GetAsync("/api/v1/processing/maintenance");
        var families = (await ApiTestClient.Json(state))["families"]!.AsArray();
        var cleanup = families.Single(f => f!["family"]!.GetValue<string>() == "unclaimed_handbacks")!;

        Assert.False(cleanup["enabled"]!.GetValue<bool>());
        Assert.Equal(7200, cleanup["interval_seconds"]!.GetValue<long>());
        Assert.Null(cleanup["next_run_at"]);
    }

    [Fact]
    public async Task The_work_file_sweep_says_it_removes_a_kept_failed_copy_once_it_is_a_day_old()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        using var state = await client.GetAsync("/api/v1/processing/maintenance");
        var families = (await ApiTestClient.Json(state))["families"]!.AsArray();
        var description = families.Single(f => f!["family"]!.GetValue<string>() == "work_temp_stale_sweep")!["description"]!.GetValue<string>();

        Assert.Contains("including a failed copy you asked Weir to keep", description, StringComparison.Ordinal);
        Assert.DoesNotContain("while you keep failed work files", description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Maintenance_lists_the_leftover_work_file_sweep_and_the_unclaimed_copy_cleanup_and_nothing_for_failed_downloads()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        using var state = await client.GetAsync("/api/v1/processing/maintenance");
        var families = (await ApiTestClient.Json(state))["families"]!.AsArray().Select(f => f!["family"]!.GetValue<string>()).ToList();

        Assert.Equal(["work_temp_stale_sweep", "unclaimed_handbacks"], families);
    }

    [Fact]
    public async Task The_removed_failed_download_cleanup_settings_are_accepted_ignored_and_no_longer_reported()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        using var saved = await client.PutAsync(
            "/api/v1/processing/operator-settings",
            new { csrf_token = await client.CsrfAsync(), failure_cleanup_enabled = true, failure_cleanup_interval_seconds = 86400 });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var body = await ApiTestClient.Json(saved);
        Assert.False(body.AsObject().ContainsKey("failure_cleanup_enabled"));
        Assert.False(body.AsObject().ContainsKey("failure_cleanup_interval_seconds"));
    }

    [Fact]
    public async Task A_run_of_the_removed_failed_download_cleanup_is_refused()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        using var refused = await client.PostAsync(
            "/api/v1/processing/maintenance/run", new { csrf_token = await client.CsrfAsync(), family = "failure_cleanup", media_scope = "movie" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
    }
}
