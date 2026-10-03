using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;

namespace Weir.Contract.Tests.MediaManagers;

/// <summary>What the live Connections views are told: usage on each connection, and a frame on the Activity stream per call.</summary>
[ContractArea("media_managers")]
public sealed class ConnectionActivityTests(NoWebhookSecretFixture fixture) : IClassFixture<NoWebhookSecretFixture>
{
    private const string HealthPath = "/api/integrations/external/health";
    private const string ActivityStream = $"{WeirClient.Api}/activity/stream";
    private const string ConnectionFrame = "connection.activity";

    private static readonly HashSet<string> FrameFields = ["kind", "id", "phase", "direction", "at", "ms"];

    private WeirServer Server => fixture.Server;

    /// <summary>A signed-in operator with no media manager connections left over.</summary>
    private async Task<WeirClient> OperatorAsync()
    {
        var admin = await Server.CreateAdminClientAsync();
        await ManagerConnections.ClearAsync(admin);
        return admin;
    }

    /// <summary>The next <paramref name="count"/> <c>connection.activity</c> frames that are about one media manager connection.</summary>
    private static async Task<List<JsonObject>> ConnectionFramesAsync(SseReader stream, int connectionId, int count)
    {
        var frames = new List<JsonObject>();
        while (frames.Count < count)
        {
            // Read raw blocks: other events on the stream carry data that is not a JSON object.
            var block = await stream.NextBlockAsync();
            if (!block.Contains($"event: {ConnectionFrame}"))
            {
                continue;
            }

            var data = block.Where(line => line.StartsWith("data:", StringComparison.Ordinal)).Select(line => line["data:".Length..].Trim());
            var frame = JsonNode.Parse(string.Join('\n', data))!.AsObject();
            if ((string?)frame["kind"] == "media_manager" && (int?)frame["id"] == connectionId)
            {
                frames.Add(frame);
            }
        }

        return frames;
    }

    /// <summary>Reads the frames every stream opens with, so it is certainly listening when the test acts.</summary>
    private static async Task OpenedAsync(SseReader stream)
    {
        Assert.Equal(["retry: 5000"], await stream.NextBlockAsync());
        await stream.NextEventNamedAsync("activity.latest");
    }

    private static async Task<int> AddManagerAsync(WeirClient admin, FakeManager fake)
    {
        var created = await ManagerConnections.CreateAsync(
            admin, new JsonObject { ["base_url"] = fake.BaseUrl, ["api_key"] = fake.ApiKey });
        Assert.True(created.Status == HttpStatusCode.Created, created.ToString());
        return JsonFields.Id(created.Fields);
    }

    [Fact]
    public async Task A_manager_never_used_reports_no_answer_time_and_no_last_use()
    {
        using var admin = await OperatorAsync();

        var created = await ManagerConnections.CreateAsync(admin);

        Assert.True(created.Status == HttpStatusCode.Created, created.ToString());
        JsonFields.AssertNull(created.Fields, "last_answer_ms");
        JsonFields.AssertNull(created.Fields, "last_used_at");
    }

    [Fact]
    public async Task A_download_client_never_used_reports_no_answer_time_and_no_last_use()
    {
        using var admin = await OperatorAsync();

        var created = await admin.PostWithCsrfAsync(
            $"{WeirClient.Api}/download-clients/connections",
            new JsonObject { ["kind"] = "qbittorrent", ["base_url"] = "http://192.0.2.30:8080" });

        Assert.True(created.Status == HttpStatusCode.Created, created.ToString());
        JsonFields.AssertNull(created.Fields, "last_answer_ms");
        JsonFields.AssertNull(created.Fields, "last_used_at");
    }

    [Fact]
    public async Task Testing_a_manager_records_how_long_it_took_and_when()
    {
        using var admin = await OperatorAsync();
        using var fake = FakeManager.StartDeluno();
        var connectionId = await AddManagerAsync(admin, fake);

        var tested = await admin.PostWithCsrfAsync($"{ManagerConnections.Route}/{connectionId}/test");

        Assert.True(tested.Status == HttpStatusCode.OK && (bool)tested.Fields["ok"]!, tested.ToString());
        var row = (await admin.GetAsync($"{ManagerConnections.Route}/{connectionId}")).Fields;
        Assert.True(JsonFields.IsWholeNumber(row["last_answer_ms"]) && (long)row["last_answer_ms"]! >= 0, row.ToJsonString());
        Assert.EndsWith("Z", (string)row["last_used_at"]!);
        var listed = Assert.Single((await admin.GetAsync(ManagerConnections.Route)).Elements)!;
        Assert.Equal(row["last_answer_ms"]!.ToJsonString(), listed["last_answer_ms"]!.ToJsonString());
        Assert.Equal((string)row["last_used_at"]!, (string)listed["last_used_at"]!);
    }

    [Fact]
    public async Task Testing_a_manager_sends_an_asked_frame_and_then_an_answered_one()
    {
        using var admin = await OperatorAsync();
        using var fake = FakeManager.StartDeluno();
        var connectionId = await AddManagerAsync(admin, fake);

        JsonObject asked, answered;
        using (var stream = await admin.OpenStreamAsync(ActivityStream))
        {
            await OpenedAsync(stream);
            Assert.Equal(HttpStatusCode.OK, (await admin.PostWithCsrfAsync($"{ManagerConnections.Route}/{connectionId}/test")).Status);
            var frames = await ConnectionFramesAsync(stream, connectionId, 2);
            (asked, answered) = (frames[0], frames[1]);
        }

        Assert.Equal(FrameFields, JsonFields.Names(asked));
        Assert.Equal("asked", (string)asked["phase"]!);
        Assert.Equal("outbound", (string)asked["direction"]!);
        JsonFields.AssertNull(asked, "ms");
        Assert.Equal("answered", (string)answered["phase"]!);
        Assert.Equal("outbound", (string)answered["direction"]!);
        Assert.True(JsonFields.IsWholeNumber(answered["ms"]) && (long)answered["ms"]! >= 0, answered.ToJsonString());
        Assert.EndsWith("Z", (string)answered["at"]!);
    }

    [Fact]
    public async Task A_manager_that_answers_with_an_error_sends_a_failed_frame()
    {
        using var admin = await OperatorAsync();
        using var fake = FakeManager.StartDeluno();
        fake.Route("GET", HealthPath, new Reply(500));
        var connectionId = await AddManagerAsync(admin, fake);

        JsonObject ended;
        using (var stream = await admin.OpenStreamAsync(ActivityStream))
        {
            await OpenedAsync(stream);
            Assert.Equal(HttpStatusCode.OK, (await admin.PostWithCsrfAsync($"{ManagerConnections.Route}/{connectionId}/test")).Status);
            ended = (await ConnectionFramesAsync(stream, connectionId, 2))[1];
        }

        Assert.Equal("failed", (string)ended["phase"]!);
        Assert.Equal("outbound", (string)ended["direction"]!);
        Assert.True(JsonFields.IsWholeNumber(ended["ms"]), ended.ToJsonString());
    }

    [Fact]
    public async Task A_manager_calling_weir_with_its_secret_sends_an_inbound_answered_frame()
    {
        using var admin = await OperatorAsync();
        var (row, headers) = await ManagerConnections.CreateWithSecretAsync(
            admin, new JsonObject { ["kind"] = "radarr", ["base_url"] = "http://192.0.2.20:7878" });

        JsonObject frame;
        using (var stream = await admin.OpenStreamAsync(ActivityStream))
        {
            await OpenedAsync(stream);
            var posted = await admin.PostAsync(
                $"{WeirClient.Api}/intake/webhook/radarr",
                new JsonObject { ["eventType"] = "Grab", ["movie"] = new JsonObject { ["id"] = 1 } },
                headers);
            Assert.True(posted.Status == HttpStatusCode.OK, posted.ToString());
            frame = Assert.Single(await ConnectionFramesAsync(stream, JsonFields.Id(row), 1));
        }

        Assert.Equal(FrameFields, JsonFields.Names(frame));
        Assert.Equal("answered", (string)frame["phase"]!);
        Assert.Equal("inbound", (string)frame["direction"]!);
        JsonFields.AssertNull(frame, "ms");
        Assert.Equal("media_manager", (string)frame["kind"]!);
        var used = (string?)(await admin.GetAsync($"{ManagerConnections.Route}/{JsonFields.Id(row)}")).Fields["last_used_at"];
        Assert.NotNull(used);
        Assert.EndsWith("Z", used);
    }
}
