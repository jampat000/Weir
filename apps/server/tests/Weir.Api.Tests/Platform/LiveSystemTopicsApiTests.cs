using System.Net;
using Weir.Infrastructure.Activity;
using static Weir.Api.Tests.Platform.ApiTestClient;
using static Weir.Api.Tests.Platform.LiveStreamApiTests;

namespace Weir.Api.Tests.Platform;

/// <summary>
/// What System › Logs, the diagnostics and About's update and network notices need from the stream: a <c>data.changed</c> frame
/// when the job queue changes, when the counters move, when the update state or the network choice changes, and when an update
/// check finds a newer release.
/// </summary>
public sealed class LiveSystemTopicsApiTests
{
    private const string Enqueue = "/api/v1/processing/jobs/file-remux-pass/enqueue";

    private static async Task<(WeirTestServer Server, ApiTestClient Client, StreamReader Stream)> StartListeningAsync()
    {
        var (server, client) = await StartSignedInAsync();
        var stream = await OpenStreamAsync(server, client);
        await NextFrameAsync(stream, "server.hello");
        return (server, client, stream);
    }

    /// <summary>Reads topics until <paramref name="wanted"/> comes by, skipping the others a busy server also sends.</summary>
    private static async Task NextTopicNamedAsync(StreamReader stream, string wanted)
    {
        while (await NextTopicAsync(stream) != wanted)
        {
        }
    }

    [Fact]
    public async Task Queueing_a_job_through_the_api_reaches_an_open_stream_as_a_jobs_change()
    {
        var watched = Directory.CreateTempSubdirectory("weir-live-jobs-").FullName;
        var (server, client, stream) = await StartListeningAsync();
        await using var _server = server;
        using var _stream = stream;
        try
        {
            await TestDatabase.ExecuteAsync(server, "UPDATE libraries SET watched_folder = $w WHERE media_type = 'movie'", ("$w", watched));

            using var queued = await client.PostAsync(Enqueue, new { csrf_token = await client.CsrfAsync(), relative_media_path = "live/queued.mkv" });

            Assert.Equal(HttpStatusCode.OK, queued.StatusCode);
            await NextTopicNamedAsync(stream, DataTopics.Jobs);
        }
        finally
        {
            Directory.Delete(watched, recursive: true);
        }
    }

    [Fact]
    public async Task Cancelling_a_queued_job_through_the_api_reaches_an_open_stream_as_a_jobs_change()
    {
        var watched = Directory.CreateTempSubdirectory("weir-live-cancel-").FullName;
        var (server, client, stream) = await StartListeningAsync();
        await using var _server = server;
        using var _stream = stream;
        try
        {
            await TestDatabase.ExecuteAsync(server, "UPDATE libraries SET watched_folder = $w WHERE media_type = 'movie'", ("$w", watched));
            using var queued = await client.PostAsync(Enqueue, new { csrf_token = await client.CsrfAsync(), relative_media_path = "live/cancelled.mkv" });
            Assert.Equal(HttpStatusCode.OK, queued.StatusCode);
            await NextTopicNamedAsync(stream, DataTopics.Jobs);
            var jobId = await TestDatabase.ScalarAsync(server, "SELECT id FROM jobs WHERE job_kind = 'processing.file.remux_pass.v1'");

            using var cancelled = await client.PostAsync($"/api/v1/processing/jobs/{jobId}/cancel-pending", new { csrf_token = await client.CsrfAsync() });

            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            await NextTopicNamedAsync(stream, DataTopics.Jobs);
        }
        finally
        {
            Directory.Delete(watched, recursive: true);
        }
    }

    [Fact]
    public async Task The_counters_moving_reach_an_open_stream_as_a_metrics_change()
    {
        var (server, client, stream) = await StartListeningAsync();
        await using var _server = server;
        using var _stream = stream;

        using var request = await client.GetAsync("/api/v1/processing/libraries");

        Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        await NextTopicNamedAsync(stream, DataTopics.Metrics);
    }

    [Fact]
    public async Task Saving_the_update_settings_reaches_an_open_stream_as_an_update_change()
    {
        var (server, client, stream) = await StartListeningAsync();
        await using var _server = server;
        using var _stream = stream;

        using var saved = await client.PutAsync(
            "/api/v1/suite/update-settings",
            new { csrf_token = await client.CsrfAsync(), mode = "NotifyOnly", check_on_startup = true });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        await NextTopicNamedAsync(stream, DataTopics.Update);
    }

    [Fact]
    public async Task An_update_the_tray_downloaded_reaches_an_open_stream_as_an_update_change_and_shows_in_the_update_state()
    {
        var (server, client, stream) = await StartListeningAsync();
        await using var _server = server;
        using var _stream = stream;

        await File.WriteAllTextAsync(Path.Join(server.Home, "update-state.json"), "{\"downloaded\": true, \"version\": \"9.9.9\"}");

        await NextTopicNamedAsync(stream, DataTopics.Update);
        var state = await Json(await client.GetAsync("/api/v1/suite/update-state"));
        Assert.Equal(("true", "9.9.9"), (state["downloaded"]!.ToString(), state["pending_version"]!.ToString()));
    }

    [Fact]
    public async Task A_choice_saved_for_the_tray_reaches_an_open_stream_as_a_network_access_change()
    {
        var (server, _, stream) = await StartListeningAsync();
        await using var _server = server;
        using var _stream = stream;

        await File.WriteAllTextAsync(Path.Join(server.Home, "lan-access"), "on");

        await NextTopicNamedAsync(stream, DataTopics.NetworkAccess);
    }
}
