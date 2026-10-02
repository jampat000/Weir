using System.Net;

namespace Weir.Api.Tests.Platform;

/// <summary>
/// The space to keep free is each workflow's own setting, no longer one value in Setup › Performance › Speed: a workflow reports
/// and saves it, and Performance accepts the old field without acting on it.
/// </summary>
public sealed class WorkflowFreeSpaceApiTests
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

    private static async Task<HttpResponseMessage> CreateAsync(ApiTestClient client, string name, int? freeSpaceMb = null)
    {
        var body = new Dictionary<string, object>
        {
            ["csrf_token"] = await client.CsrfAsync(),
            ["name"] = name,
            ["media_type"] = "movie",
            ["watched_folder"] = $@"c:\{name}-in",
            ["output_folder"] = $@"c:\{name}-out",
        };
        if (freeSpaceMb is { } megabytes)
        {
            body["minimum_free_disk_space_mb"] = megabytes;
        }

        return await client.PostAsync(Libraries, body);
    }

    [Fact]
    public async Task A_new_workflow_keeps_five_gigabytes_free()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        using var created = await CreateAsync(client, "Anime");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(5120, (await ApiTestClient.Json(created))["minimum_free_disk_space_mb"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_workflow_saves_the_space_it_keeps_free_and_zero_turns_the_check_off()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        using var created = await CreateAsync(client, "Anime", 20480);
        using var off = await CreateAsync(client, "Kids", 0);

        Assert.Equal(20480, (await ApiTestClient.Json(created))["minimum_free_disk_space_mb"]!.GetValue<int>());
        Assert.Equal(0, (await ApiTestClient.Json(off))["minimum_free_disk_space_mb"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_048_577)]
    public async Task A_space_outside_what_a_drive_can_be_asked_to_keep_is_refused(int megabytes)
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        using var refused = await CreateAsync(client, "Anime", megabytes);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
    }

    [Fact]
    public async Task Performance_no_longer_reports_the_space_to_keep_free_or_the_cost_of_an_unknown_resolution()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        using var response = await client.GetAsync(OperatorSettings);

        var body = (await ApiTestClient.Json(response)).AsObject();
        Assert.DoesNotContain("minimum_free_disk_space_mb", body.Select(pair => pair.Key));
        Assert.DoesNotContain("runner_cost_undetermined", body.Select(pair => pair.Key));
    }

    [Fact]
    public async Task Performance_accepts_the_old_fields_and_leaves_what_a_workflow_keeps_free_alone()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;
        using var created = await CreateAsync(client, "Anime", 2048);

        using var saved = await client.PutAsync(
            OperatorSettings,
            new { csrf_token = await client.CsrfAsync(), minimum_free_disk_space_mb = 999, runner_cost_undetermined = 9 });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(2048, await TestDatabase.ScalarAsync(server, "SELECT minimum_free_disk_space_mb FROM libraries WHERE name = 'Anime'"));
        Assert.NotEqual(999, await TestDatabase.ScalarAsync(server, "SELECT minimum_free_disk_space_mb FROM operator_settings WHERE id = 1"));
    }
}
