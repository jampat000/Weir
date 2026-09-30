using System.Net;
using System.Text.Json.Nodes;

namespace Weir.Api.Tests.Platform;

/// <summary>
/// A workflow saved back exactly as the API reported it comes out the same: every setting the response carries is one the
/// save reads, so a client that echoes the response never resets a setting it did not touch.
/// </summary>
public sealed class LibraryRoundTripApiTests
{
    private const string Libraries = "/api/v1/processing/libraries";

    /// <summary>What the response reports about a workflow that a save never takes: the server works these out itself.</summary>
    private static readonly string[] ReportedOnly =
    [
        "id", "display_order", "effective_min_file_size_mb", "effective_min_file_age_seconds", "manager_coverage",
        "manager_coverage_detail", "discovered_from_connection_id", "discovered_library_key", "active_job_count",
        "next_look_at", "periodic_scan", "next_scan_at", "updated_at",
    ];

    /// <summary>Settings a workflow used to report. The server accepts them on a save and ignores them.</summary>
    private static readonly string[] RemovedSettings =
    [
        "rewrite_with_ffmpeg", "hardware_decode_mode", "hardware_device", "hardware_disabled_vendors_csv",
    ];

    [Fact]
    public async Task Saving_a_workflow_back_as_reported_keeps_every_setting()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        using var created = await client.PostAsync(
            Libraries,
            new
            {
                csrf_token = await client.CsrfAsync(),
                name = "anime",
                media_type = "tv",
                watched_folder = @"c:\anime-in",
                output_folder = @"c:\anime-out",
                remux_writer = "ffmpeg",
                min_file_size_mb = 7,
                rejected_file_action = "delete_file",
                remove_original_after_success = false,
            });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var stored = await ApiTestClient.Json(created);
        var path = $"{Libraries}/{stored["id"]!.GetValue<long>()}";

        var body = stored.AsObject().DeepClone().AsObject();
        foreach (var reportedOnly in ReportedOnly)
        {
            body.Remove(reportedOnly);
        }

        body["csrf_token"] = await client.CsrfAsync();
        using var saved = await client.PutAsync(path, body);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var after = (await ApiTestClient.Json(saved)).AsObject();
        foreach (var (name, value) in stored.AsObject().Where(pair => !ReportedOnly.Contains(pair.Key)))
        {
            Assert.True(JsonNode.DeepEquals(value, after[name]), $"{name} changed from {value?.ToJsonString()} to {after[name]?.ToJsonString()}");
        }
    }

    [Fact]
    public async Task A_workflow_no_longer_reports_settings_that_have_no_effect()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();

        using var created = await client.PostAsync(Libraries, await NewWorkflowAsync(client));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var reported = (await ApiTestClient.Json(created)).AsObject();
        foreach (var removed in RemovedSettings)
        {
            Assert.False(reported.ContainsKey(removed), removed);
        }
    }

    [Fact]
    public async Task A_save_that_still_sends_the_removed_settings_is_accepted_and_they_are_ignored()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var body = await NewWorkflowAsync(client);
        body["hardware_decode_mode"] = "device";
        body["hardware_device"] = "cuda";
        body["hardware_disabled_vendors_csv"] = "nvidia";
        body["rewrite_with_ffmpeg"] = false;

        using var created = await client.PostAsync(Libraries, body);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var reported = (await ApiTestClient.Json(created)).AsObject();
        foreach (var removed in RemovedSettings)
        {
            Assert.False(reported.ContainsKey(removed), removed);
        }
    }

    private static async Task<JsonObject> NewWorkflowAsync(ApiTestClient client) =>
        new()
        {
            ["csrf_token"] = await client.CsrfAsync(),
            ["name"] = "anime",
            ["media_type"] = "tv",
            ["watched_folder"] = @"c:\anime-in",
            ["output_folder"] = @"c:\anime-out",
        };
}
