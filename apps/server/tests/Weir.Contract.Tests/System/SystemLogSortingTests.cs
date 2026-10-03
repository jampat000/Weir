using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.SystemArea.SystemLogSeed;
using static Weir.Contract.Tests.SystemArea.SystemLogSortModel;
using static Weir.Contract.Tests.SystemArea.SystemPartBJson;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>System, Logs in each order it can be listed in: sorted by any column, in either direction, paged by cursor.</summary>
[ContractArea("system")]
public sealed class SystemLogSortingTests(SystemLogSortingTests.SeededSortingFixture fixture)
    : IClassFixture<SystemLogSortingTests.SeededSortingFixture>
{
    private const string Log = SystemPartBHelpers.Api + "/system/log";
    private const string Export = SystemPartBHelpers.Api + "/system/log/export";

    private const int RowsPerSource = 12;
    private const int Rows = RowsPerSource * 3;
    private const int SmallPage = 5;
    private const int WholeLog = 100;

    // Names that differ by case, so ignoring case is visible: "Bravo" comes before "alpha" only when case counts.
    private static readonly string[] Workflows = ["alpha", "Bravo", "charlie"];

    private static readonly string[] EventTypes = ["auth.login_succeeded", "library.scan_completed", "library.watched_folder_added"];
    private static readonly string[] EventResults = ["failed", "success", "warning", "running"];
    private static readonly string[] JobKinds =
        ["processing.file.remux_pass.v1", "processing.work_temp_stale_sweep.v1", "processing.library.clean.v1"];

    private static readonly (string Status, string? LastError)[] JobStatuses =
        [("failed", "ffmpeg stopped"), ("pending", null), ("completed", null), ("cancelled", null)];

    private static readonly string[] ServerLoggers =
        ["weir.processing", "weir.platform.suite_settings.backups", "weir.library_mode.router"];

    private static readonly string[] ServerLevels = ["ERROR", "WARNING", "INFO"];

    public static TheoryData<string, string> EveryOrder
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var sort in Sorts)
            {
                foreach (var direction in Directions)
                {
                    data.Add(sort, direction);
                }
            }

            return data;
        }
    }

    public static TheoryData<string> EachDirection => new() { "asc", "desc" };

    public static TheoryData<string> EachSort
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var sort in Sorts)
            {
                data.Add(sort);
            }

            return data;
        }
    }

    private WeirServer Server => fixture.Server;

    private static async Task<JsonObject> GetAsync(WeirClient client, params (string Name, object Value)[] parameters)
    {
        var response = await client.GetAsync(Log, InWindow(parameters));
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        return response.Fields;
    }

    private static List<string> Ids(JsonObject body) =>
        [.. body["items"]!.AsArray().Select(item => (string)item!["id"]!)];

    private static List<JsonNode> Items(JsonObject body) => [.. body["items"]!.AsArray().Select(item => item!)];

    private static (string Name, object Value)[] Parameters(string? sort, string? direction, string? cursor = null)
    {
        var parameters = new List<(string Name, object Value)>();
        if (sort is not null)
        {
            parameters.Add(("sort", sort));
        }

        if (direction is not null)
        {
            parameters.Add(("direction", direction));
        }

        if (cursor is not null)
        {
            parameters.Add(("cursor", cursor));
        }

        return [.. parameters];
    }

    [Fact]
    public async Task The_seeded_log_has_rows_that_tie_on_every_sort()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var items = Items(await GetAsync(admin, ("limit", WholeLog)));

        var bySource = items.GroupBy(item => (string)item["source"]!).ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(new Dictionary<string, int> { ["event"] = RowsPerSource, ["job"] = RowsPerSource, ["server"] = RowsPerSource }, bySource);
        Assert.Equal(LevelRank.Keys.ToHashSet(), items.Select(item => (string)item["level"]!).ToHashSet());
        Assert.True(items.Select(item => (string)item["category"]!).Distinct().Count() >= 4);
        Assert.Equal(Workflows.ToHashSet(), items.Where(HasWorkflow).Select(item => (string)item["workflow"]!["name"]!).ToHashSet());
        Assert.Contains(items, item => item["workflow"] is null);
        Assert.True(items.Select(When).Distinct().Count() < Rows);
    }

    [Theory]
    [MemberData(nameof(EveryOrder))]
    public async Task Rows_come_in_the_order_the_sort_and_direction_ask_for(string sort, string direction)
    {
        using var admin = await Server.CreateAdminClientAsync();

        var items = Items(await GetAsync(admin, ("limit", WholeLog), ("sort", sort), ("direction", direction)));

        Assert.Equal(Expected(items, sort, direction), items.Select(item => (string)item["id"]!));
    }

    [Fact]
    public async Task The_list_is_newest_first_when_it_is_not_asked_to_sort()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var plain = await GetAsync(admin, ("limit", WholeLog));
        var explicitOrder = await GetAsync(admin, ("limit", WholeLog), ("sort", "time"), ("direction", "desc"));
        var oldestFirst = await GetAsync(admin, ("limit", WholeLog), ("direction", "asc"));

        Assert.Equal(Ids(plain), Ids(explicitOrder));
        Assert.Equal(Enumerable.Reverse(Ids(plain)), Ids(oldestFirst));
    }

    [Fact]
    public async Task Levels_run_from_errors_down_to_successes_when_ascending()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var items = Items(await GetAsync(admin, ("limit", WholeLog), ("sort", "level"), ("direction", "asc")));

        var ranks = items.Select(item => LevelRank[(string)item["level"]!]).ToList();
        Assert.Equal(ranks.Order(), ranks);
        Assert.Equal(0, ranks[0]);
        Assert.Equal(LevelRank["success"], ranks[^1]);
    }

    [Theory]
    [MemberData(nameof(EachDirection))]
    public async Task Rows_that_tie_on_the_sort_value_fall_by_time_in_the_direction_of_the_sort(string direction)
    {
        using var admin = await Server.CreateAdminClientAsync();

        var items = Items(await GetAsync(admin, ("limit", WholeLog), ("sort", "level"), ("direction", direction)));

        foreach (var level in LevelRank.Keys)
        {
            var times = items.Where(item => (string)item["level"]! == level).Select(When).ToList();
            var expected = direction == "desc" ? times.OrderByDescending(time => time) : times.Order();
            Assert.True(expected.SequenceEqual(times), level);
        }
    }

    [Theory]
    [MemberData(nameof(EachDirection))]
    public async Task Rows_with_no_workflow_come_last_whichever_way_the_workflow_sort_runs(string direction)
    {
        using var admin = await Server.CreateAdminClientAsync();

        var items = Items(await GetAsync(admin, ("limit", WholeLog), ("sort", "workflow"), ("direction", direction)));

        var named = items.Select(item => IsTruthy(item["workflow"])).ToList();
        Assert.Equal(named.OrderByDescending(isNamed => isNamed), named);
        Assert.True(named.Any(isNamed => isNamed) && !named.All(isNamed => isNamed));
        var names = items.Where(item => IsTruthy(item["workflow"])).Select(item => ((string)item["workflow"]!["name"]!).ToLowerInvariant()).ToList();
        var sorted = direction == "desc" ? names.OrderByDescending(name => name, StringComparer.Ordinal) : names.Order(StringComparer.Ordinal);
        Assert.Equal(sorted, names);
    }

    [Theory]
    [MemberData(nameof(EveryOrder))]
    public async Task Paging_in_any_order_visits_every_row_once_and_adds_up_to_the_whole_list(string sort, string direction)
    {
        using var admin = await Server.CreateAdminClientAsync();
        var whole = Ids(await GetAsync(admin, ("limit", WholeLog), ("sort", sort), ("direction", direction)));
        var walked = new List<string>();
        string? cursor = null;
        for (var attempt = 0; attempt < Rows; attempt++)
        {
            var page = await GetAsync(admin, [("limit", SmallPage), .. Parameters(sort, direction, cursor)]);
            walked.AddRange(Ids(page));
            Assert.Equal(Rows, (int)page["total"]!);
            cursor = (string?)page["next_cursor"];
            if (cursor is null)
            {
                break;
            }
        }

        Assert.Null(cursor);
        Assert.Equal(whole, walked);
        Assert.Equal(Rows, walked.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(EachSort))]
    public async Task A_filter_and_a_sort_page_together(string sort)
    {
        using var admin = await Server.CreateAdminClientAsync();
        (string Name, object Value)[] filters = [("source", "event,job"), ("level", "error,warning")];
        var whole = Ids(await GetAsync(admin, [("limit", WholeLog), ("sort", sort), ("direction", "asc"), .. filters]));
        var walked = new List<string>();
        string? cursor = null;
        for (var attempt = 0; attempt < Rows; attempt++)
        {
            var page = await GetAsync(admin, [("limit", 3), ("sort", sort), ("direction", "asc"), .. filters, .. Parameters(null, null, cursor)]);
            walked.AddRange(Ids(page));
            cursor = (string?)page["next_cursor"];
            if (cursor is null)
            {
                break;
            }
        }

        Assert.Equal(whole, walked);
        Assert.True(whole.Count > 0 && whole.Count < Rows);
    }

    [Theory]
    [InlineData("message", null)]
    [InlineData("Level", null)]
    [InlineData("", null)]
    [InlineData(null, "sideways")]
    [InlineData(null, "ASC")]
    [InlineData(null, "")]
    [InlineData("level", "up")]
    public async Task A_sort_or_direction_the_log_does_not_have_is_refused(string? sort, string? direction)
    {
        using var admin = await Server.CreateAdminClientAsync();

        foreach (var url in new[] { Log, Export })
        {
            var response = await admin.GetAsync(url, InWindow(Parameters(sort, direction)));

            Assert.True(response.Status == HttpStatusCode.UnprocessableEntity, response.ToString());
            AssertTruthy(response.Fields["detail"], "detail");
            Assert.Equal("query", (string?)response.Fields["detail"]![0]!["loc"]![0]);
        }
    }

    [Theory]
    [InlineData("level", "desc")]
    [InlineData("category", "asc")]
    [InlineData("time", "asc")]
    [InlineData("time", "desc")]
    [InlineData(null, "asc")]
    [InlineData(null, null)]
    public async Task A_cursor_made_for_another_sort_or_direction_is_refused(string? sort, string? direction)
    {
        using var admin = await Server.CreateAdminClientAsync();
        var cursor = (string)(await GetAsync(admin, ("limit", SmallPage), ("sort", "level"), ("direction", "asc")))["next_cursor"]!;

        var response = await admin.GetAsync(Log, InWindow(Parameters(sort, direction, cursor)));

        Assert.True(response.Status == HttpStatusCode.UnprocessableEntity, response.ToString());
        Assert.Equal(["query", "cursor"], Strings(response.Fields["detail"]![0]!["loc"]));
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("e30")]
    [InlineData("W10")]
    [InlineData("eyJzb3J0IjoibGV2ZWwifQ")]
    [InlineData("AAAA")]
    [InlineData("!!")]
    public async Task A_cursor_the_log_did_not_give_out_is_refused_under_every_sort(string cursor)
    {
        using var admin = await Server.CreateAdminClientAsync();

        foreach (var sort in Sorts)
        {
            var response = await admin.GetAsync(Log, InWindow(("sort", sort), ("cursor", cursor)));

            Assert.True(response.Status == HttpStatusCode.UnprocessableEntity, $"{sort}: {response}");
        }
    }

    [Theory]
    [MemberData(nameof(EveryOrder))]
    public async Task The_export_lists_the_rows_in_the_order_the_screen_shows_them(string sort, string direction)
    {
        using var admin = await Server.CreateAdminClientAsync();
        var onScreen = Ids(await GetAsync(admin, ("limit", WholeLog), ("sort", sort), ("direction", direction)));

        var asJson = await admin.GetAsync(Export, InWindow(("format", "json"), ("sort", sort), ("direction", direction)));
        var asCsv = await admin.GetAsync(Export, InWindow(("format", "csv"), ("sort", sort), ("direction", direction)));

        Assert.True(asJson.Status == HttpStatusCode.OK, asJson.ToString());
        Assert.Equal(onScreen, asJson.Elements.Select(row => (string)row!["id"]!));
        Assert.True(asCsv.Status == HttpStatusCode.OK, asCsv.ToString());
        var titles = CsvTable.Parse(asCsv.Text).Select(row => row["title"]);
        var byId = (await GetAsync(admin, ("limit", WholeLog)))["items"]!.AsArray()
            .ToDictionary(item => (string)item!["id"]!, item => (string)item!["title"]!);
        Assert.Equal(onScreen.Select(id => byId[id]), titles);
    }

    /// <summary>Twelve events, jobs and server lines that share levels, categories, workflows and instants, so every sort has ties.</summary>
    public sealed class SeededSortingFixture : SeededServerFixture
    {
        protected override Task SeedAsync(StoppedDatabase database)
        {
            var connection = database.Connection;
            SystemPartBHelpers.SeedUsers(connection);
            SeedSql.Execute(connection, "DELETE FROM jobs");
            var workflows = new List<long>();
            foreach (var name in Workflows)
            {
                SeedSql.Execute(connection, "INSERT INTO libraries (name, media_type) VALUES ($name, 'movie')", ("$name", name));
                workflows.Add(Convert.ToInt64(
                    SeedSql.Scalar(connection, "SELECT id FROM libraries WHERE name = $name", ("$name", name)), CultureInfo.InvariantCulture));
            }

            var lines = new List<string>();
            for (var index = 0; index < RowsPerSource; index++)
            {
                var moment = At(10 + (index / 4));
                long? workflow = index % 4 == 3 ? null : workflows[index % 3];
                InsertEvent(connection, moment, EventTypes[index % 3], $"Event {index}", EventResults[index % 4], libraryId: workflow);
                var (status, lastError) = JobStatuses[index % 4];
                InsertJob(connection, moment, $"contract:sort:{index}", JobKinds[index % 3], status, lastError, workflow);
                lines.Add(LogLine(moment, ServerLevels[index % 3], ServerLoggers[(index / 3) % 3], $"Line {index}"));
            }

            AppendToServerLog(Server, lines);
            return Task.CompletedTask;
        }
    }
}
