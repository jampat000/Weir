using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Weir.Api.Endpoints;
using Weir.Infrastructure.SystemReadings;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.Platform;

/// <summary>The System view's readings over HTTP: the endpoint, the stream's <c>system.stats</c> frame, and their JSON.</summary>
public sealed class SystemStatsApiTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static StatsNow SomeNow(double? cpu = 12.4, long? diskRead = 1000) => new(
        Noon, cpu, 16, 8_000, 16_000, diskRead, 500, 7.5, 3.1, 123_000, 41.0, 2_000, 1_500, 148.0, 2, 4);

    private static readonly SystemStatsSnapshot Snapshot = new(
        SomeNow(),
        [StatsSample.Of(SomeNow()).Point],
        new MachineFacts("Windows 11 Pro", 3600, null),
        [new DriveReading("D:", "D:\\", 2000, 500, 30, 100, 12.5, null, null, null, [new WorkflowOnDrive(1, "Movies", ["watched", "output"])])]);

    private static StatsUpdate Update(StatsSample sample) => new(1, sample, Snapshot.Machine, Snapshot.Drives);

    [Fact]
    public void The_snapshot_is_the_documented_json()
    {
        var json = (JsonObject)JsonNode.Parse(Weir.Core.Json.WireJsonWriter.Dumps(SystemStatsWire.Snapshot(Snapshot), Weir.Core.Json.WireJsonFormat.Compact))!;

        Assert.Equal(1000, json["interval_ms"]!.GetValue<int>());
        Assert.Equal(600, json["window_s"]!.GetValue<int>());
        Assert.Equal("2026-10-02T12:00:00Z", json["now"]!["at"]!.GetValue<string>());
        Assert.Equal(12.4, json["now"]!["cpu_percent"]!.GetValue<double>());
        Assert.Equal(16, json["now"]!["cores"]!.GetValue<int>());
        Assert.Equal(41.0, json["now"]!["tools_cpu_percent"]!.GetValue<double>());
        Assert.Equal(148.0, json["now"]!["processing_speed"]!.GetValue<double>());
        Assert.Equal((2, 4), (json["now"]!["running"]!.GetValue<int>(), json["now"]!["slots"]!.GetValue<int>()));
        Assert.Equal(50.0, json["history"]![0]!["memory_percent"]!.GetValue<double>());
        Assert.Equal("Windows 11 Pro", json["machine"]!["os"]!.GetValue<string>());
        Assert.Null(json["machine"]!["reboot_pending"]);
        Assert.True(((JsonObject)json["machine"]!).ContainsKey("reboot_pending"));
    }

    [Fact]
    public void A_drive_reads_as_the_documented_json_with_nulls_where_nothing_could_be_read()
    {
        var json = (JsonObject)JsonNode.Parse(Weir.Core.Json.WireJsonWriter.Dumps(SystemStatsWire.Snapshot(Snapshot), Weir.Core.Json.WireJsonFormat.Compact))!;

        var drive = (JsonObject)json["drives"]![0]!;

        Assert.Equal(("D:", "D:\\", 2000, 500, 30, 100), (
            drive["name"]!.GetValue<string>(), drive["path"]!.GetValue<string>(), drive["total_bytes"]!.GetValue<int>(),
            drive["free_bytes"]!.GetValue<int>(), drive["weir_bytes"]!.GetValue<int>(), drive["keep_free_bytes"]!.GetValue<int>()));
        Assert.Equal(12.5, drive["full_in_days"]!.GetValue<double>());
        Assert.All(["read_bytes_per_sec", "write_bytes_per_sec", "busy_percent"], key =>
        {
            Assert.True(drive.ContainsKey(key));
            Assert.Null(drive[key]);
        });
        Assert.Equal(["watched", "output"], drive["workflows"]![0]!["roles"]!.AsArray().Select(role => role!.GetValue<string>()));
    }

    [Fact]
    public void A_reading_the_machine_could_not_give_is_null_in_the_json()
    {
        var update = Update(StatsSample.Of(SomeNow(cpu: null, diskRead: null)));

        var frame = SystemStatsFrames.Frame(update);

        var data = JsonNode.Parse(frame.Split('\n')[1]["data: ".Length..])!;
        Assert.Null(data["now"]!["cpu_percent"]);
        Assert.Null(data["now"]!["disk_read_bytes_per_sec"]);
        Assert.Null(data["point"]!["cpu_percent"]);
        Assert.Equal(1_500, data["now"]!["processing_write_bytes_per_sec"]!.GetValue<int>());
    }

    [Fact]
    public void A_frame_carries_the_readings_of_the_moment_and_the_point_for_the_traces()
    {
        var frame = SystemStatsFrames.Frame(Update(StatsSample.Of(SomeNow())));

        Assert.StartsWith("event: system.stats\ndata: {\"now\":{\"at\":\"2026-10-02T12:00:00Z\"", frame, StringComparison.Ordinal);
        Assert.EndsWith("\n\n", frame, StringComparison.Ordinal);
        var data = JsonNode.Parse(frame.Split('\n')[1]["data: ".Length..])!;
        Assert.Equal("2026-10-02T12:00:00Z", data["point"]!["at"]!.GetValue<string>());
        Assert.Equal(148.0, data["point"]!["processing_speed"]!.GetValue<double>());
    }

    [Fact]
    public void A_frame_carries_the_machine_and_the_drives_as_they_stand()
    {
        var frame = SystemStatsFrames.Frame(Update(StatsSample.Of(SomeNow())));

        var data = JsonNode.Parse(frame.Split('\n')[1]["data: ".Length..])!;
        Assert.Equal("Windows 11 Pro", data["machine"]!["os"]!.GetValue<string>());
        Assert.Equal(3600, data["machine"]!["uptime_seconds"]!.GetValue<long>());
        Assert.Equal(("D:", 500), (data["drives"]![0]!["name"]!.GetValue<string>(), data["drives"]![0]!["free_bytes"]!.GetValue<int>()));
    }

    [Fact]
    public async Task A_stream_hears_of_drives_the_sampler_read_with_the_next_reading()
    {
        var store = new SystemStatsStore(TimeProvider.System, 4);
        store.Add(StatsSample.Of(SomeNow()));
        using var stop = new CancellationTokenSource();
        await using var frames = new SystemStatsFrames(store).ForAsync(stop.Token).GetAsyncEnumerator(stop.Token);
        Assert.True(await frames.MoveNextAsync());
        Assert.Contains("\"drives\":[]", frames.Current, StringComparison.Ordinal);

        store.SetDrives(Snapshot.Drives);
        store.Add(StatsSample.Of(SomeNow(cpu: 20)));

        Assert.True(await frames.MoveNextAsync());
        Assert.Contains("\"name\":\"D:\"", frames.Current, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stream_opened_gets_the_newest_reading_first_and_then_each_new_one()
    {
        var store = new SystemStatsStore(TimeProvider.System, 4);
        store.Add(StatsSample.Of(SomeNow(cpu: 10)));
        using var stop = new CancellationTokenSource();
        await using var frames = new SystemStatsFrames(store).ForAsync(stop.Token).GetAsyncEnumerator(stop.Token);

        Assert.True(await frames.MoveNextAsync());
        Assert.Contains("\"cpu_percent\":10", frames.Current, StringComparison.Ordinal);

        var next = frames.MoveNextAsync().AsTask();
        Assert.False(next.IsCompleted);
        store.Add(StatsSample.Of(SomeNow(cpu: 20)));

        Assert.True(await next);
        Assert.Contains("\"cpu_percent\":20", frames.Current, StringComparison.Ordinal);
    }

    private static async Task<(WeirTestServer Server, ApiTestClient Client)> StartSignedInAsync()
    {
        var server = await WeirTestServer.StartAsync(
            [("WEIR_SESSION_SECRET", Secret), ("WEIR_PROCESSING_WORKER_COUNT", "0")],
            configureServices: services => services.AddSingleton<IHostReadingSource>(new StubHostSource()));
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        return (server, client);
    }

    [Fact]
    public async Task The_stats_need_a_signed_in_user()
    {
        await using var server = await StartServerAsync();

        using var response = await server.Client.GetAsync("/api/v1/system/stats");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_stats_endpoint_serves_the_machine_the_sampler_reads()
    {
        var (server, client) = await StartSignedInAsync();
        await using var _server = server;

        using var response = await client.GetAsync("/api/v1/system/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await Json(response);
        Assert.Equal("Stub OS", json["machine"]!["os"]!.GetValue<string>());
        Assert.Equal(Environment.ProcessorCount, json["now"]!["cores"]!.GetValue<int>());
        Assert.Equal(2_000, json["now"]!["memory_total_bytes"]!.GetValue<long>());
        Assert.Equal(1_000, json["now"]!["memory_used_bytes"]!.GetValue<long>());
        Assert.NotEmpty(json["history"]!.AsArray());
    }

    [Fact]
    public async Task The_activity_stream_sends_a_system_stats_frame_as_soon_as_it_opens()
    {
        var (server, client) = await StartSignedInAsync();
        await using var _server = server;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/activity/stream");
        request.Headers.Add("Cookie", string.Join("; ", client.Cookies.Select(pair => $"{pair.Key}={pair.Value}")));
        using var response = await server.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());

        var data = await ReadFrameDataAsync(reader, "system.stats").WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2_000, data["now"]!["memory_total_bytes"]!.GetValue<long>());
        Assert.Equal(50.0, data["point"]!["memory_percent"]!.GetValue<double>());
    }

    private static async Task<JsonNode> ReadFrameDataAsync(StreamReader reader, string wanted)
    {
        string? eventName = null;
        while (await reader.ReadLineAsync() is { } line)
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

    /// <summary>A machine with half its memory in use, so no test depends on the one it runs on.</summary>
    private sealed class StubHostSource : IHostReadingSource
    {
        public CpuTimes? ReadCpuTimes() => new(10, 20);

        public MemoryReading? ReadMemory() => new(2_000, 1_000);

        public long? ReadUptimeSeconds() => 99;

        public string? ReadOperatingSystem() => "Stub OS";

        public bool? ReadRebootPending() => false;

        public DiskSnapshot ReadDisks() => DiskSnapshot.Empty;

        public string? DriveRootOf(string path) => null;

        public string? VolumeKeyOf(string driveRoot) => null;

        public DriveSpace? ReadSpace(string driveRoot) => null;
    }
}
