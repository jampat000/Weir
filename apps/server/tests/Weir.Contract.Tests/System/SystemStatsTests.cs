using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.SystemArea.SystemPartBJson;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>
/// The System view's readings: GET /system/stats and the system.stats frame on the Activity stream. The server
/// reads the machine it runs on, so these tests judge shapes, types and ranges, never the numbers.
/// </summary>
[ContractArea("system")]
public sealed class SystemStatsTests(ServerFixture fixture) : IClassFixture<ServerFixture>
{
    private const string Stats = SystemPartBHelpers.Api + "/system/stats";
    private const string Libraries = SystemPartBHelpers.Api + "/processing/libraries";

    private static readonly string[] NowKeys =
    [
        "at", "cpu_percent", "cores", "memory_used_bytes", "memory_total_bytes", "disk_read_bytes_per_sec",
        "disk_write_bytes_per_sec", "disk_busy_percent", "weir_cpu_percent", "weir_memory_bytes", "tools_cpu_percent",
        "processing_read_bytes_per_sec", "processing_write_bytes_per_sec", "processing_speed", "running", "slots",
    ];

    private static readonly string[] PointKeys =
    [
        "at", "cpu_percent", "memory_percent", "disk_read_bytes_per_sec", "disk_write_bytes_per_sec",
        "processing_read_bytes_per_sec", "processing_write_bytes_per_sec", "processing_speed",
    ];

    private static readonly string[] MachineKeys = ["os", "uptime_seconds", "reboot_pending"];

    private static readonly string[] DriveKeys =
    [
        "name", "path", "total_bytes", "free_bytes", "weir_bytes", "keep_free_bytes", "full_in_days",
        "read_bytes_per_sec", "write_bytes_per_sec", "busy_percent", "workflows",
    ];

    private static readonly string[] PercentKeys =
        ["cpu_percent", "memory_percent", "disk_busy_percent", "weir_cpu_percent", "tools_cpu_percent"];

    // Drives are read every 30 seconds, so a workflow created now shows on its drive within about that long.
    private static readonly TimeSpan DriveReadWait = TimeSpan.FromSeconds(50);

    private WeirServer Server => fixture.Server;

    /// <summary>A figure is a non-negative number, or null where the machine could not be read; a percentage is at most 100.</summary>
    private static void AssertReadingIsSane(JsonNode? values)
    {
        foreach (var (key, value) in values!.AsObject())
        {
            if (key == "at")
            {
                Assert.True(IsString(value), key);
            }
            else if (value is not null)
            {
                Assert.True(IsNumber(value), $"{key}: {value.ToJsonString()}");
                Assert.True((double)value >= 0, $"{key}: {value.ToJsonString()}");
                if (PercentKeys.Contains(key))
                {
                    Assert.True((double)value <= 100, $"{key}: {value.ToJsonString()}");
                }
            }
        }
    }

    private static DateTimeOffset Moment(JsonNode? text) => DateTimeOffset.Parse((string)text!, CultureInfo.InvariantCulture);

    private static async Task<JsonObject> StatsOfAsync(WeirClient admin)
    {
        var response = await admin.GetAsync(Stats);
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        return response.Fields;
    }

    [Fact]
    public async Task System_stats_require_authentication()
    {
        using var client = Server.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Stats)).Status);
    }

    [Fact]
    public async Task System_stats_have_the_documented_shape()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var body = await StatsOfAsync(admin);

        AssertKeys(["interval_ms", "window_s", "now", "history", "machine", "drives"], body);
        Assert.Equal(1000, (int)body["interval_ms"]!);
        Assert.Equal(600, (int)body["window_s"]!);
        AssertKeys(NowKeys, body["now"]);
        AssertReadingIsSane(body["now"]);
        Assert.True((double)body["now"]!["cores"]! >= 1);
        Assert.True((double)body["now"]!["slots"]! >= 0);
        Assert.True((double)body["now"]!["running"]! >= 0);
        AssertKeys(MachineKeys, body["machine"]);
    }

    [Fact]
    public async Task The_history_has_a_time_on_every_point_oldest_first()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var body = await StatsOfAsync(admin);
        var history = body["history"]!.AsArray();

        Assert.InRange(history.Count, 1, 600);
        foreach (var point in history)
        {
            AssertKeys(PointKeys, point);
            AssertReadingIsSane(point);
        }

        var times = history.Select(point => Moment(point!["at"])).ToList();
        Assert.Equal(times.Order(), times);
        // The newest point is the reading the response was taken from.
        Assert.Equal((string?)body["now"]!["at"], (string?)history[^1]!["at"]);
    }

    [Fact]
    public async Task The_machine_says_what_it_can()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var machine = (await StatsOfAsync(admin))["machine"]!;

        Assert.True(machine["os"] is null || IsString(machine["os"]));
        Assert.True(machine["uptime_seconds"] is null || (double)machine["uptime_seconds"]! >= 0);
        Assert.True(machine["reboot_pending"] is null || IsBool(machine["reboot_pending"]));
    }

    [Fact]
    public async Task Every_drive_that_holds_a_workflow_folder_is_listed_with_its_workflows()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var drives = await Poll.UntilAsync(
            async () =>
            {
                var listed = (await StatsOfAsync(admin))["drives"]!.AsArray();
                return listed.Count > 0 ? listed : null;
            },
            "the first drive reading",
            DriveReadWait);

        foreach (var drive in drives)
        {
            AssertKeys(DriveKeys, drive);
            Assert.True((double)drive!["total_bytes"]! >= (double)drive["free_bytes"]!);
            Assert.True((double)drive["free_bytes"]! >= 0);
            Assert.True((double)drive["weir_bytes"]! >= 0);
            Assert.True((double)drive["keep_free_bytes"]! >= 0);
            AssertTruthy(drive["name"], "drive name");
            AssertTruthy(drive["workflows"], "a drive is listed only because a workflow keeps a folder on it");
            foreach (var workflow in drive["workflows"]!.AsArray())
            {
                AssertKeys(["id", "name", "roles"], workflow);
                AssertTruthy(workflow!["roles"], "roles");
                Assert.True(Strings(workflow["roles"]).ToHashSet().IsSubsetOf(["watched", "work", "output"]));
            }
        }

        // The seeded workflows keep their work files under Weir's own folder, so they are on a drive.
        var onADrive = drives
            .SelectMany(drive => drive!["workflows"]!.AsArray())
            .Select(workflow => (string)workflow!["name"]!)
            .ToHashSet();
        Assert.True(onADrive.IsSupersetOf(["Movies", "TV"]));
    }

    [Fact]
    public async Task A_new_workflows_folders_appear_on_their_drive_and_ask_for_the_free_space_it_wants()
    {
        using var admin = await Server.CreateAdminClientAsync();
        using var folders = new TemporaryFolder();
        var watched = Directory.CreateDirectory(Path.Combine(folders.Path, "in")).FullName;
        var output = Directory.CreateDirectory(Path.Combine(folders.Path, "out")).FullName;
        var created = await admin.PostWithCsrfAsync(
            Libraries,
            new JsonObject
            {
                ["enabled"] = false,
                ["name"] = "Stats Probe",
                ["media_type"] = "movie",
                ["watched_folder"] = watched,
                ["output_folder"] = output,
                ["minimum_free_disk_space_mb"] = 2048,
            });
        Assert.True(created.Status is HttpStatusCode.OK or HttpStatusCode.Created, created.ToString());

        var found = await Poll.UntilAsync(
            async () =>
            {
                foreach (var drive in (await StatsOfAsync(admin))["drives"]!.AsArray())
                {
                    foreach (var workflow in drive!["workflows"]!.AsArray())
                    {
                        if ((string?)workflow!["name"] == "Stats Probe")
                        {
                            return new JsonObject { ["drive"] = drive.DeepClone(), ["workflow"] = workflow.DeepClone() };
                        }
                    }
                }

                return null;
            },
            "the new workflow's drive",
            DriveReadWait);

        Assert.True(Strings(found["workflow"]!["roles"]).ToHashSet().IsSupersetOf(["watched", "output"]));
        Assert.True((double)found["drive"]!["keep_free_bytes"]! >= 2048d * 1024 * 1024);
    }

    [Fact]
    public async Task The_activity_stream_carries_system_stats_frames_a_second_apart()
    {
        using var admin = await Server.CreateAdminClientAsync();
        JsonObject first;
        JsonObject second;
        using (var stream = await SystemPartBStreams.OpenAsync(admin))
        {
            first = (await stream.NextEventNamedAsync("system.stats")).AsObject();
            second = (await stream.NextEventNamedAsync("system.stats")).AsObject();
        }

        foreach (var frame in new[] { first, second })
        {
            AssertKeys(["now", "point", "machine", "drives"], frame);
            AssertKeys(MachineKeys, frame["machine"]);
            AssertKeys(NowKeys, frame["now"]);
            AssertKeys(PointKeys, frame["point"]);
            AssertReadingIsSane(frame["now"]);
            AssertReadingIsSane(frame["point"]);
            Assert.Equal((string?)frame["now"]!["at"], (string?)frame["point"]!["at"]);
        }

        Assert.True(Moment(second["now"]!["at"]) > Moment(first["now"]!["at"]));
    }

    [Fact]
    public async Task System_stats_are_documented_in_the_openapi_schema()
    {
        using var client = Server.CreateClient();

        var schema = (await client.GetAsync("/openapi.json")).Fields;

        Assert.True(schema["paths"]!.AsObject().ContainsKey("/api/v1/system/stats"));
        var schemas = schema["components"]!["schemas"]!;
        AssertKeys(NowKeys, schemas["SystemStatsNowOut"]!["properties"]);
        AssertKeys(PointKeys, schemas["SystemStatsPointOut"]!["properties"]);
        AssertKeys(["now", "point", "machine", "drives"], schemas["SystemStatsFrame"]!["properties"]);
    }

    [Fact]
    public async Task System_stats_only_answer_get()
    {
        using var admin = await Server.CreateAdminClientAsync();

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await admin.PostAsync(Stats)).Status);
    }
}
