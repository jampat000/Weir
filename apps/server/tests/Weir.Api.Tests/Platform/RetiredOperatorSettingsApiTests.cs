using System.Net;

namespace Weir.Api.Tests.Platform;

/// <summary>
/// "Verbose file-detection records" was removed on 23 Sep 2026: nothing ever acted on it. The settings no longer show it,
/// and an older client that still sends it is answered as though it had not, rather than refused.
/// </summary>
public sealed class RetiredOperatorSettingsApiTests
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
    public async Task Verbose_detection_logging_is_no_longer_a_setting()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        using var response = await client.GetAsync("/api/v1/processing/operator-settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await ApiTestClient.Json(response)).AsObject().ContainsKey("verbose_detection_logging"));
    }

    [Fact]
    public async Task An_older_client_that_still_sends_it_is_answered_and_nothing_is_saved()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        using var alone = await client.PutAsync(
            "/api/v1/processing/operator-settings", new { csrf_token = await client.CsrfAsync(), verbose_detection_logging = true });
        Assert.Equal(HttpStatusCode.OK, alone.StatusCode);
        Assert.False((await ApiTestClient.Json(alone)).AsObject().ContainsKey("verbose_detection_logging"));

        using var alongside = await client.PutAsync(
            "/api/v1/processing/operator-settings",
            new { csrf_token = await client.CsrfAsync(), verbose_detection_logging = true, min_file_age_seconds = 120 });
        Assert.Equal(HttpStatusCode.OK, alongside.StatusCode);
        Assert.Equal(120, (await ApiTestClient.Json(alongside))["min_file_age_seconds"]!.GetValue<int>());

        // The column is still there for configuration backups that carry it, and keeps its default.
        Assert.Equal(0, await TestDatabase.ScalarAsync(server, "SELECT verbose_detection_logging FROM operator_settings WHERE id = 1"));
    }
}
