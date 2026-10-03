using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.SystemArea.SystemPartBJson;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>What System shows: the overview, the scheduled tasks, and the system.tasks and system.log frames.</summary>
[ContractArea("system")]
public sealed class SystemOverviewApiTests(ServerFixture fixture) : IClassFixture<ServerFixture>
{
    private const string Overview = SystemPartBHelpers.Api + "/system/overview";
    private const string Tasks = SystemPartBHelpers.Api + "/system/tasks";
    private const string RemuxPass = "processing.file.remux_pass.v1";
    private const string ScanDispatch = "processing.watched_folder.remux_scan_dispatch.v1";

    private static readonly string[] OverviewFields =
    [
        "version", "update", "uptime_seconds", "started_at", "runs_as", "address", "data_bytes", "browsers_live",
        "requests", "jobs_today", "restarts_this_week", "checks",
    ];

    private static readonly string[] TaskFields =
        ["key", "label", "running", "last_run_at", "last_ok", "last_error", "next_run_at", "interval_seconds"];

    private static readonly string[] UpdateStatuses =
        ["checking", "up_to_date", "update_available", "downloaded", "not_published", "unavailable"];

    // The in-process workers, and the cleanup timers, which the shared server leaves off.
    // A workflow's scan task records the scheduled scan of its watched folder, which first falls due a couple of minutes
    // after the folders are saved; a scan queued by hand carries no workflow and does not count towards the task.
    private static readonly TimeSpan ScanFinishTimeout = TimeSpan.FromMinutes(6);

    private static readonly Dictionary<string, string> WorkersOn = new() { ["WEIR_PROCESSING_WORKER_COUNT"] = "1" };

    private static readonly Dictionary<string, string> CleanupTimersOn = new(WorkersOn)
    {
        ["WEIR_PROCESSING_WORK_TEMP_STALE_SWEEP_MOVIE_SCHEDULE_ENABLED"] = "1",
        ["WEIR_PROCESSING_WORK_TEMP_STALE_SWEEP_TV_SCHEDULE_ENABLED"] = "1",
    };

    public static TheoryData<string> Paths => new() { Overview, Tasks };

    private WeirServer Server => fixture.Server;

    private static DateTimeOffset At(JsonNode? moment) => DateTimeOffset.Parse((string)moment!, CultureInfo.InvariantCulture);

    private static async Task<JsonObject> OverviewOfAsync(WeirClient client)
    {
        var response = await client.GetAsync(Overview);
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        return response.Fields;
    }

    private static async Task<JsonArray> TasksOfAsync(WeirClient client)
    {
        var response = await client.GetAsync(Tasks);
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        return response.Elements;
    }

    private static JsonNode? TaskWithKey(JsonArray tasks, string key, bool mustHaveSucceeded = false) =>
        tasks.FirstOrDefault(task => (string?)task!["key"] == key && (!mustHaveSucceeded || IsTruthy(task["last_ok"])));

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Both_endpoints_need_a_signed_in_user(string path)
    {
        using var client = Server.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).Status);
    }

    [Fact]
    public async Task The_overview_has_the_documented_fields_and_types()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var overview = await OverviewOfAsync(admin);

        Assert.Equal(OverviewFields, KeysInOrder(overview));
        Assert.True(IsString(overview["version"]) && IsTruthy(overview["version"]));
        Assert.Equal(["status", "latest_version"], KeysInOrder(overview["update"]));
        Assert.Contains((string?)overview["update"]!["status"], UpdateStatuses);
        Assert.True(IsInteger(overview["uptime_seconds"]) && (long)overview["uptime_seconds"]! >= 0);
        Assert.EndsWith("Z", (string)overview["started_at"]!, StringComparison.Ordinal);
        Assert.Contains((string?)overview["runs_as"], new[] { "service", "app", "docker" });
        Assert.True(IsInteger(overview["data_bytes"]) && (long)overview["data_bytes"]! > 0);
        Assert.Equal(["median_ms", "p95_ms", "errors_today"], KeysInOrder(overview["requests"]));
        Assert.True((double)overview["requests"]!["median_ms"]! <= (double)overview["requests"]!["p95_ms"]!);
        Assert.Equal(["run", "failed"], KeysInOrder(overview["jobs_today"]));
        Assert.Equal(["passing", "total"], KeysInOrder(overview["checks"]));
        var checks = overview["checks"]!;
        Assert.True(0 <= (long)checks["passing"]! && (long)checks["passing"]! <= (long)checks["total"]!);
    }

    [Fact]
    public async Task The_overview_says_where_this_weir_listens()
    {
        using var admin = await Server.CreateAdminClientAsync();

        // The contract server listens on 127.0.0.1 only, which a browser reaches as localhost.
        Assert.Equal($"http://localhost:{Server.BaseUrl.Port}", (string?)(await OverviewOfAsync(admin))["address"]);
    }

    [Fact]
    public async Task A_server_started_by_hand_runs_as_an_app()
    {
        using var admin = await Server.CreateAdminClientAsync();

        Assert.Equal("app", (string?)(await OverviewOfAsync(admin))["runs_as"]);
    }

    [Fact]
    public async Task The_overview_counts_every_browser_holding_the_stream_open()
    {
        using var admin = await Server.CreateAdminClientAsync();
        Assert.Equal(0, (int)(await OverviewOfAsync(admin))["browsers_live"]!);

        using (var first = await SystemPartBStreams.OpenAsync(admin))
        {
            await SystemPartBStreams.AssertOpenedAsync(first);
            Assert.Equal(1, (int)(await OverviewOfAsync(admin))["browsers_live"]!);
            using var second = await SystemPartBStreams.OpenAsync(admin);
            await SystemPartBStreams.AssertOpenedAsync(second);
            Assert.Equal(2, (int)(await OverviewOfAsync(admin))["browsers_live"]!);
        }

        await Poll.UntilAsync(
            async () => (int)(await OverviewOfAsync(admin))["browsers_live"]! == 0, "the closed streams to stop counting");
    }

    [Fact]
    public async Task Uptime_and_restarts_follow_the_server_through_a_restart()
    {
        await using var server = await WeirServer.StartNewAsync();
        JsonObject first;
        using (var admin = await server.CreateAdminClientAsync())
        {
            first = await OverviewOfAsync(admin);
        }

        Assert.Equal(0, (int)first["restarts_this_week"]!);

        await server.RestartAsync();
        using var signedIn = server.CreateClient();
        await signedIn.LoginAsync();
        var second = await OverviewOfAsync(signedIn);

        Assert.Equal(1, (int)second["restarts_this_week"]!);
        Assert.True(At(second["started_at"]) > At(first["started_at"]));
        Assert.True((double)second["uptime_seconds"]! < 120);
    }

    [Fact]
    public async Task Jobs_today_counts_the_jobs_that_finished_and_the_ones_that_failed()
    {
        await using var server = await WeirServer.StartNewAsync();
        JsonNode before;
        using (var admin = await server.CreateAdminClientAsync())
        {
            before = (await OverviewOfAsync(admin))["jobs_today"]!.DeepClone();
        }

        await using (var database = await server.StopForDatabaseAsync())
        {
            var connection = database.Connection;
            SystemPartBLibraries.InsertJob(connection, "overview-1", RemuxPass, "completed");
            SystemPartBLibraries.InsertJob(connection, "overview-2", RemuxPass, "completed");
            SystemPartBLibraries.InsertJob(connection, "overview-3", RemuxPass, "failed");
            // A scan that found nothing wrong is left out, as it is from the Jobs list.
            SystemPartBLibraries.InsertJob(connection, "overview-4", ScanDispatch, "completed");
            SystemPartBLibraries.InsertJob(connection, "overview-5", RemuxPass, "pending");
        }

        using var signedIn = server.CreateClient();
        await signedIn.LoginAsync();

        var after = (await OverviewOfAsync(signedIn))["jobs_today"]!;

        Assert.Equal(
            (3, 1),
            ((long)after["run"]! - (long)before["run"]!, (long)after["failed"]! - (long)before["failed"]!));
    }

    [Fact]
    public async Task The_tasks_list_has_each_task_with_its_last_run_and_its_next()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var log = await Poll.UntilAsync(
            async () => TaskWithKey(await TasksOfAsync(admin), "suite-log-retention", mustHaveSucceeded: true),
            "the log retention task to finish its first run");
        var tasks = await TasksOfAsync(admin);

        Assert.All(tasks, task => Assert.Equal(TaskFields, KeysInOrder(task)));
        Assert.Equal("Trim the log", (string?)log["label"]);
        Assert.False((bool)log["running"]!);
        AssertNull(log, "last_error");
        Assert.Equal(3600, (int)log["interval_seconds"]!);
        Assert.EndsWith("Z", (string)log["last_run_at"]!, StringComparison.Ordinal);
        Assert.True(At(log["next_run_at"]) > At(log["last_run_at"]));
        var labels = tasks.Select(task => (string)task!["label"]!).ToHashSet();
        Assert.True(labels.IsSupersetOf(["Scan Movies", "Scan TV", "Tidy finished jobs", "Clear old sign-ins"]));
        // Plumbing that polls every few seconds is not listed.
        var keys = tasks.Select(task => (string)task!["key"]!).ToHashSet();
        Assert.Empty(keys.Intersect(["activity-latest-poll", "connection-usage-flush", "artwork-resolver"]));
    }

    [Fact]
    public async Task A_switched_on_cleanup_is_listed_with_its_label_and_how_often_it_runs()
    {
        await using var server = await WeirServer.StartNewAsync(CleanupTimersOn);
        using var admin = await server.CreateAdminClientAsync();

        var cleanup = await Poll.UntilAsync(
            async () => TaskWithKey(await TasksOfAsync(admin), "cleanup-leftover-files", mustHaveSucceeded: true),
            "the cleanup to finish its first run");

        Assert.Equal("Clear leftover files", (string?)cleanup["label"]);
        Assert.Equal(3600, (int)cleanup["interval_seconds"]!);
        AssertNull(cleanup, "last_error");
        Assert.True(At(cleanup["next_run_at"]) > At(cleanup["last_run_at"]));
        // The cleanup for unclaimed copies stays off until a person switches it on.
        Assert.Null(TaskWithKey(await TasksOfAsync(admin), "cleanup-unclaimed-copies"));
    }

    [Fact]
    public async Task A_scan_that_runs_reaches_an_open_stream_as_a_system_tasks_frame()
    {
        await using var server = await WeirServer.StartNewAsync(WorkersOn);
        using var admin = await server.CreateAdminClientAsync();
        using var folders = new TemporaryFolder();
        var watched = Directory.CreateDirectory(Path.Combine(folders.Path, "watched")).FullName;
        var output = Directory.CreateDirectory(Path.Combine(folders.Path, "output")).FullName;
        var libraryId = await SystemPartBLibraries.SetMovieFoldersAsync(admin, watched, output);
        var scan = $"scan-{libraryId}";
        await Poll.UntilAsync(
            async () => TaskWithKey(await TasksOfAsync(admin), scan), "the workflow's scan to be listed");
        var before = TaskWithKey(await TasksOfAsync(admin), scan)!["last_run_at"]?.GetValue<string>();

        using var stream = await SystemPartBStreams.OpenAsync(admin);
        await SystemPartBStreams.AssertOpenedAsync(stream);
        var queued = await admin.PostWithCsrfAsync(
            $"{SystemPartBHelpers.Api}/processing/jobs/watched-folder-remux-scan-dispatch/enqueue", new JsonObject());
        Assert.True(queued.Status == HttpStatusCode.OK, queued.ToString());
        JsonNode? task = null;
        JsonArray frame;
        var deadline = DateTime.UtcNow + ScanFinishTimeout;
        do
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for the scan to finish in a system.tasks frame; the last task seen was {task?.ToJsonString()}");
            frame = (await stream.NextEventNamedAsync("system.tasks", ScanFinishTimeout)).AsArray();
            task = TaskWithKey(frame, scan);
        }
        while (!(task is not null
            && !(bool)task["running"]!
            && IsTruthy(task["last_run_at"])
            && (before is null || At(task["last_run_at"]) > DateTimeOffset.Parse(before, CultureInfo.InvariantCulture))));

        Assert.Equal("Scan Movies", (string?)task["label"]);
        Assert.True(IsTruthy(task["last_ok"]) && (bool)task["last_ok"]!);
        AssertNull(task, "last_error");
        Assert.All(frame, entry => Assert.Equal(TaskFields, KeysInOrder(entry)));
    }

    [Fact]
    public async Task A_warning_the_server_logs_reaches_an_open_stream_as_a_system_log_frame()
    {
        using var admin = await Server.CreateAdminClientAsync();
        using var stream = await SystemPartBStreams.OpenAsync(admin);
        await SystemPartBStreams.AssertOpenedAsync(stream);
        using (var other = Server.CreateClient())
        {
            await other.LoginAsync(password: "not-the-password", expected: HttpStatusCode.Unauthorized);
        }

        var frame = await stream.NextEventNamedAsync("system.log");

        Assert.Equal(["at", "level", "message"], KeysInOrder(frame));
        Assert.Equal("WARNING", (string?)frame["level"]);
        Assert.Equal("auth event: login failed", (string?)frame["message"]);
        Assert.EndsWith("Z", (string)frame["at"]!, StringComparison.Ordinal);
    }
}
