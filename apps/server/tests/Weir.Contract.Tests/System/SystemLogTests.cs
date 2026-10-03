using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.SystemArea.SystemLogSeed;
using static Weir.Contract.Tests.SystemArea.SystemPartBJson;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>System, Logs: Weir's events, jobs and server log as one list, filtered, counted, paged and exported.</summary>
[ContractArea("system")]
public sealed class SystemLogTests(SystemLogTests.SeededLogFixture fixture) : IClassFixture<SystemLogTests.SeededLogFixture>
{
    private const string Log = SystemPartBHelpers.Api + "/system/log";
    private const string Export = SystemPartBHelpers.Api + "/system/log/export";
    private const string RemuxPass = "processing.file.remux_pass.v1";

    private WeirServer Server => fixture.Server;

    private long LibraryId => fixture.LibraryId;

    private static List<string> Titles(JsonObject body) =>
        [.. body["items"]!.AsArray().Select(item => (string)item!["title"]!)];

    private static List<string> Sources(JsonObject body) =>
        [.. body["items"]!.AsArray().Select(item => (string)item!["source"]!)];

    private static async Task<JsonObject> GetAsync(WeirClient client, params (string Name, object Value)[] parameters)
    {
        var response = await client.GetAsync(Log, InWindow(parameters));
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        return response.Fields;
    }

    [Fact]
    public async Task The_log_needs_a_session()
    {
        using var client = Server.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Log)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Export)).Status);
    }

    [Fact]
    public async Task The_log_is_one_newest_first_list_of_every_source()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var body = await GetAsync(admin);

        AssertKeys(["items", "next_cursor", "total", "counts"], body);
        Assert.Equal(["server", "server", "job", "job", "job", "event", "event", "event"], Sources(body));
        var times = body["items"]!.AsArray().Select(item => (string)item!["at"]!).ToList();
        Assert.Equal(times.OrderByDescending(time => time, StringComparer.Ordinal), times);
        Assert.Equal(8, (int)body["total"]!);
        AssertNull(body, "next_cursor");
        Assert.DoesNotContain("Heat processed", Titles(body));
        var details = string.Join(" ", body["items"]!.AsArray().Select(item => (string?)item!["detail"] ?? string.Empty));
        Assert.DoesNotContain("Check watched folders", details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_row_carries_the_record_of_its_source()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var body = await GetAsync(admin);

        var items = body["items"]!.AsArray();
        var eventRow = items.Last(item => (string?)item!["source"] == "event")!;
        var jobRow = items.Last(item => (string?)item!["source"] == "job")!;
        var line = items.Last(item => (string?)item!["source"] == "server")!;
        AssertTruthy(eventRow["event"]!["event_type"], "event_type");
        AssertNull(eventRow, "job");
        AssertNull(eventRow, "server");
        AssertTruthy(jobRow["job"]!["id"], "job id");
        AssertNull(jobRow, "event");
        AssertNull(jobRow, "server");
        AssertTruthy(line["server"]!["logger"], "logger");
        AssertNull(line, "event");
        AssertNull(line, "job");
        var failed = items.First(item => (string?)item!["id"] == $"job:{fixture.FailedJob}")!;
        Assert.Equal("error", (string?)failed["level"]);
        Assert.Equal("processing", (string?)failed["category"]);
        Assert.Equal("ffmpeg stopped", (string?)failed["job"]!["last_error"]);
        Assert.Equal(LibraryId, (long)failed["workflow"]!["id"]!);
        AssertTruthy(failed["workflow"]!["name"], "workflow name");
    }

    [Fact]
    public async Task Counts_are_what_each_choice_would_show_with_the_other_filters_applied()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var body = await GetAsync(admin, ("source", "event"), ("level", "error"));

        Assert.Equal(1, (int)body["total"]!);
        Assert.Equal(["Sign-in failed"], Titles(body));
        AssertSameJson(Parse("""{"event": 1, "job": 1, "server": 1}"""), body["counts"]!["source"]);
        Assert.Equal(1, (int)body["counts"]!["level"]!["error"]!);
        Assert.Equal(2, (int)body["counts"]!["level"]!["success"]!);
        Assert.Equal(1, (int)body["counts"]!["category"]!["sign_in"]!);
        AssertKeys(
            ["processing", "scans", "cleanup", "library", "connections", "backups", "sign_in", "updates", "weir"],
            body["counts"]!["category"]);
    }

    [Fact]
    public async Task Workflow_counts_are_what_choosing_a_workflow_would_show_with_the_other_filters_applied()
    {
        using var admin = await Server.CreateAdminClientAsync();
        var library = LibraryId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        AssertSameJson(Parse($$"""{"{{library}}": 3}"""), (await GetAsync(admin))["counts"]!["workflow"]);
        AssertSameJson(
            Parse($$"""{"{{library}}": 3}"""),
            (await GetAsync(admin, ("workflow", library)))["counts"]!["workflow"]); // the workflow's own filter is left out
        AssertSameJson(Parse($$"""{"{{library}}": 1}"""), (await GetAsync(admin, ("level", "error")))["counts"]!["workflow"]);
        AssertSameJson(Parse("{}"), (await GetAsync(admin, ("source", "server")))["counts"]!["workflow"]); // server lines belong to no workflow
    }

    [Fact]
    public async Task The_filters_narrow_every_source_together()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var errors = await GetAsync(admin, ("level", "error"));
        var cleanup = await GetAsync(admin, ("category", "cleanup"));
        var backups = await GetAsync(admin, ("q", "backup folder"));
        var workflow = await GetAsync(admin, ("workflow", LibraryId));
        var several = await GetAsync(admin, ("level", "error,warning"));

        Assert.Equal(new HashSet<string> { "event", "job", "server" }, Sources(errors).ToHashSet());
        Assert.All(errors["items"]!.AsArray(), item => Assert.Equal("error", (string?)item!["level"]));
        Assert.Equal(["job"], Sources(cleanup));
        Assert.Equal(["server"], Sources(backups));
        Assert.Equal(new HashSet<string> { "event", "job" }, Sources(workflow).ToHashSet());
        Assert.All(workflow["items"]!.AsArray(), item => Assert.Equal(LibraryId, (long)item!["workflow"]!["id"]!));
        Assert.Equal(
            new HashSet<string> { "error", "warning" },
            several["items"]!.AsArray().Select(item => (string)item!["level"]!).ToHashSet());
    }

    [Fact]
    public async Task A_filter_only_some_sources_have_leaves_the_others_out()
    {
        using var admin = await Server.CreateAdminClientAsync();

        Assert.Equal(new HashSet<string> { "event" }, Sources(await GetAsync(admin, ("trigger", "manual"))).ToHashSet());
        Assert.Equal(new HashSet<string> { "job" }, Sources(await GetAsync(admin, ("status", "failed"))).ToHashSet());
        Assert.Equal(new HashSet<string> { "server" }, Sources(await GetAsync(admin, ("has_exception", "true"))).ToHashSet());
    }

    [Fact]
    public async Task Everything_about_one_job_is_its_row_and_the_lines_written_while_it_ran()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var body = await GetAsync(admin, ("job", fixture.FailedJob));

        Assert.Equal(["job", "server"], SystemPartBJson.Sorted(Sources(body)));
    }

    [Fact]
    public async Task A_finished_routine_scan_is_left_out_unless_its_status_is_asked_for()
    {
        using var admin = await Server.CreateAdminClientAsync();

        Assert.Equal(3, (await GetAsync(admin, ("source", "job")))["items"]!.AsArray().Count);
        var completed = await GetAsync(admin, ("status", "completed"));
        Assert.Equal(
            new HashSet<string> { "processing.work_temp_stale_sweep.v1", "processing.watched_folder.remux_scan_dispatch.v1" },
            completed["items"]!.AsArray().Select(item => (string)item!["job"]!["job_kind"]!).ToHashSet());
    }

    [Fact]
    public async Task A_cursor_walks_every_row_exactly_once()
    {
        using var admin = await Server.CreateAdminClientAsync();
        var everything = (await GetAsync(admin, ("limit", 100)))["items"]!.AsArray().Select(item => (string)item!["id"]!).ToList();
        var walked = new List<string>();
        string? cursor = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var page = cursor is null
                ? await GetAsync(admin, ("limit", 3))
                : await GetAsync(admin, ("limit", 3), ("cursor", cursor));
            walked.AddRange(page["items"]!.AsArray().Select(item => (string)item!["id"]!));
            Assert.Equal(everything.Count, (int)page["total"]!);
            cursor = (string?)page["next_cursor"];
            if (cursor is null)
            {
                break;
            }
        }

        Assert.Equal(everything, walked);
    }

    [Theory]
    [InlineData("source", "disk")]
    [InlineData("level", "fatal")]
    [InlineData("category", "misc")]
    [InlineData("status", "stuck")]
    [InlineData("limit", "0")]
    [InlineData("limit", "101")]
    [InlineData("workflow", "0")]
    [InlineData("from", "yesterday")]
    [InlineData("cursor", "nonsense")]
    [InlineData("result", "great")]
    [InlineData("has_exception", "maybe")]
    public async Task A_filter_the_log_does_not_understand_is_refused(string name, string value)
    {
        using var admin = await Server.CreateAdminClientAsync();

        var response = await admin.GetAsync(Log, (name, value));

        Assert.True(response.Status == HttpStatusCode.UnprocessableEntity, response.ToString());
        AssertTruthy(response.Fields["detail"], "detail");
    }

    [Fact]
    public async Task A_viewer_can_read_the_log()
    {
        using var viewer = await SeededAccounts.SignInViewerAsync(Server);

        var response = await viewer.GetAsync(Log, Window);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(8, (int)response.Fields["total"]!);
    }

    [Fact]
    public async Task The_log_exports_as_a_spreadsheet_or_as_json_for_the_same_filters()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var asCsv = await admin.GetAsync(Export, InWindow(("level", "error"), ("format", "csv")));
        var asJson = await admin.GetAsync(Export, InWindow(("source", "job"), ("format", "json")));

        Assert.True(asCsv.Status == HttpStatusCode.OK, asCsv.ToString());
        Assert.Contains("attachment", asCsv.Header("Content-Disposition"), StringComparison.Ordinal);
        var rows = CsvTable.Parse(asCsv.Text);
        Assert.Equal(["server", "job", "event"], rows.Select(row => row["source"]));
        Assert.Equal(new HashSet<string> { "error" }, rows.Select(row => row["level"]).ToHashSet());
        Assert.Equal(HttpStatusCode.OK, asJson.Status);
        Assert.Equal(["job", "job", "job"], asJson.Elements.Select(row => (string)row!["source"]!));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.GetAsync(Export, ("format", "xml"))).Status);
    }

    [Fact]
    public async Task The_three_lists_the_log_replaces_are_still_served()
    {
        using var admin = await Server.CreateAdminClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{SystemPartBHelpers.Api}/activity/recent", ("about", "weir"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{SystemPartBHelpers.Api}/processing/jobs/inspection")).Status);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{SystemPartBHelpers.Api}/suite/logs")).Status);
    }

    public sealed class SeededLogFixture : SeededServerFixture
    {
        public long LibraryId { get; private set; }

        public long FailedJob { get; private set; }

        protected override void Seed(SqliteConnection connection)
        {
            SystemPartBHelpers.SeedUsers(connection);
            SeedSql.Execute(connection, "DELETE FROM jobs");
            LibraryId = Convert.ToInt64(SeedSql.Scalar(connection, "SELECT id FROM libraries ORDER BY id LIMIT 1"), System.Globalization.CultureInfo.InvariantCulture);
            InsertEvent(connection, At(10), "auth.login_succeeded", "Signed in", "success", "manual");
            InsertEvent(connection, At(11), "library.scan_completed", "Movies scanned", "success", "scheduled", LibraryId);
            InsertEvent(connection, At(12), "auth.login_failed", "Sign-in failed", "failed", "manual");
            InsertEvent(
                connection, At(13), "processing.file_remux_pass_completed", "Heat processed", "success",
                libraryId: LibraryId, relativePath: "Heat/heat.mkv");
            FailedJob = InsertJob(connection, At(20), "contract:failed", RemuxPass, "failed", "ffmpeg stopped", LibraryId);
            InsertJob(connection, At(21), "contract:queued", RemuxPass, "pending", libraryId: LibraryId);
            InsertJob(connection, At(22), "contract:cleanup", "processing.work_temp_stale_sweep.v1", "completed");
            InsertJob(connection, At(23), "contract:routine-scan", "processing.watched_folder.remux_scan_dispatch.v1", "completed");
            AppendToServerLog(
                Server,
                [
                    LogLine(At(30), "WARNING", "weir.platform.suite_settings.backups", "The backup folder is nearly full"),
                    LogLine(
                        At(31), "ERROR", "weir.processing", "The pass stopped",
                        ("job_id", FailedJob.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        ("traceback", "System.InvalidOperationException: ffmpeg stopped")),
                ]);
        }
    }
}
