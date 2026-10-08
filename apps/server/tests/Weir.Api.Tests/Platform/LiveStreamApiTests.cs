using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Weir.Api.Endpoints;
using Weir.Infrastructure.Activity;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.Platform;

/// <summary>The Activity stream's <c>server.hello</c> frame and the <c>data.changed</c> frames any component can send through it.</summary>
public sealed class LiveStreamApiTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    internal static async Task<(WeirTestServer Server, ApiTestClient Client)> StartSignedInAsync()
    {
        var server = await StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        return (server, client);
    }

    internal static async Task<StreamReader> OpenStreamAsync(WeirTestServer server, ApiTestClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/activity/stream");
        request.Headers.Add("Cookie", string.Join("; ", client.Cookies.Select(pair => $"{pair.Key}={pair.Value}")));
        var response = await server.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        return new StreamReader(await response.Content.ReadAsStreamAsync());
    }

    /// <summary>The data of the next frame named <paramref name="wanted"/>, skipping every other frame the stream sends.</summary>
    internal static async Task<JsonNode> NextFrameAsync(StreamReader reader, string wanted)
    {
        string? eventName = null;
        using var timeout = new CancellationTokenSource(Patience);
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                eventName = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal) && eventName == wanted)
            {
                return JsonNode.Parse(line["data: ".Length..])!;
            }
        }

        throw new InvalidOperationException("The stream ended.");
    }

    internal static async Task<string> NextTopicAsync(StreamReader reader) =>
        (await NextFrameAsync(reader, "data.changed"))["topic"]!.GetValue<string>();

    [Fact]
    public void The_frames_are_the_documented_server_sent_events()
    {
        Assert.Equal("event: server.hello\ndata: {\"boot_id\":\"abc\"}\n\n", LiveStreamFrames.HelloFrame("abc"));
        Assert.Equal("event: data.changed\ndata: {\"topic\":\"files_at_once\"}\n\n", LiveStreamFrames.ChangedFrame(DataTopics.FilesAtOnce));
    }

    [Fact]
    public async Task A_stream_that_opens_says_which_run_of_the_server_it_is_talking_to()
    {
        var (server, client) = await StartSignedInAsync();
        await using var _server = server;
        using var reader = await OpenStreamAsync(server, client);

        var hello = await NextFrameAsync(reader, "server.hello");

        var bootId = hello["boot_id"]!.GetValue<string>();
        Assert.Equal(server.Services.GetRequiredService<ServerBoot>().Id, bootId);
        Assert.True(Guid.TryParse(bootId, out _));
    }

    [Fact]
    public async Task A_published_topic_reaches_every_open_stream_after_its_hello()
    {
        var (server, client) = await StartSignedInAsync();
        await using var _server = server;
        using var first = await OpenStreamAsync(server, client);
        using var second = await OpenStreamAsync(server, client);
        await NextFrameAsync(first, "server.hello");
        await NextFrameAsync(second, "server.hello");

        var publisher = server.Services.GetRequiredService<DataChangePublisher>();
        publisher.Publish(DataTopics.Connections);
        publisher.Publish(DataTopics.Backups);

        Assert.Equal([DataTopics.Connections, DataTopics.Backups], [await NextTopicAsync(first), await NextTopicAsync(first)]);
        Assert.Equal([DataTopics.Connections, DataTopics.Backups], [await NextTopicAsync(second), await NextTopicAsync(second)]);
    }

    [Fact]
    public async Task Pausing_and_resuming_through_the_api_reach_an_open_stream_as_pause_changes()
    {
        var (server, client) = await StartSignedInAsync();
        await using var _server = server;
        using var reader = await OpenStreamAsync(server, client);
        await NextFrameAsync(reader, "server.hello");

        using var paused = await client.PutAsync("/api/v1/pause", new { csrf_token = await client.CsrfAsync(), paused = true });
        Assert.Equal(DataTopics.Pause, await NextTopicAsync(reader));

        using var resumed = await client.PutAsync("/api/v1/pause", new { csrf_token = await client.CsrfAsync(), paused = false });
        Assert.Equal(DataTopics.Pause, await NextTopicAsync(reader));
    }
}
