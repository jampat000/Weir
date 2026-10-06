using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.SystemArea.SystemLogSeed;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>System, Logs exported: every row the filters leave, up to the announced limit, in the order the list shows.</summary>
[ContractArea("system")]
public sealed class SystemLogExportTests(SystemLogExportTests.SeededExportFixture fixture)
    : IClassFixture<SystemLogExportTests.SeededExportFixture>
{
    private const string Log = SystemPartBHelpers.Api + "/system/log";
    private const string Export = SystemPartBHelpers.Api + "/system/log/export";

    // More events than one internal read of the export holds, so the export has to carry on from where a read stopped.
    private const int Events = 1100;
    private const int Jobs = 60;
    private const int ServerLines = 60;
    private const int Rows = Events + Jobs + ServerLines;
    private const int ListPage = 100;
    private const string ExportLimit = "50000";

    public static TheoryData<string, string> Orders => new()
    {
        { "time", "desc" },
        { "time", "asc" },
        { "level", "asc" },
    };

    private WeirServer Server => fixture.Server;

    /// <summary>Every row id the interactive list gives, a page of its own cap at a time.</summary>
    private static async Task<List<string>> WalkListAsync(WeirClient client, params (string Name, object Value)[] parameters)
    {
        var ids = new List<string>();
        string? cursor = null;
        for (var attempt = 0; attempt < Rows; attempt++)
        {
            (string Name, object Value)[] page = cursor is null
                ? [.. parameters, ("limit", ListPage)]
                : [.. parameters, ("limit", ListPage), ("cursor", cursor)];
            var response = await client.GetAsync(Log, InWindow(page));
            Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
            ids.AddRange(response.Fields["items"]!.AsArray().Select(item => (string)item!["id"]!));
            cursor = (string?)response.Fields["next_cursor"];
            if (cursor is null)
            {
                break;
            }
        }

        return ids;
    }

    private static async Task<JsonArray> ExportJsonAsync(WeirClient client, params (string Name, object Value)[] parameters)
    {
        var response = await client.GetAsync(Export, InWindow([.. parameters, ("format", "json")]));
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        return response.Elements;
    }

    [Fact]
    public async Task The_list_holds_a_hundred_rows_a_page_however_many_the_log_has()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var page = (await admin.GetAsync(Log, InWindow(("limit", ListPage)))).Fields;

        Assert.Equal(Rows, (int)page["total"]!);
        Assert.Equal(ListPage, page["items"]!.AsArray().Count);
        Assert.True(page["next_cursor"] is not null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.GetAsync(Log, InWindow(("limit", ListPage + 1)))).Status);
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public async Task The_json_export_holds_every_row_in_the_order_the_list_walks_them(string sort, string direction)
    {
        using var admin = await Server.CreateAdminClientAsync();

        var rows = await ExportJsonAsync(admin, ("sort", sort), ("direction", direction));

        Assert.Equal(await WalkListAsync(admin, ("sort", sort), ("direction", direction)), rows.Select(row => (string)row!["id"]!));
        Assert.Equal(Rows, rows.Count);
    }

    [Fact]
    public async Task The_csv_export_holds_every_row_and_says_how_many()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var response = await admin.GetAsync(Export, InWindow(("format", "csv")));

        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        var rows = CsvTable.Parse(response.Text);
        Assert.Equal(Rows, rows.Count);
        Assert.Equal(Rows.ToString(System.Globalization.CultureInfo.InvariantCulture), response.Header("x-weir-export-rows"));
        Assert.Equal(ExportLimit, response.Header("x-weir-export-limit"));
        Assert.Equal(new HashSet<string> { "event", "job", "server" }, rows.Select(row => row["source"]).ToHashSet());
    }

    [Fact]
    public async Task The_csv_and_json_exports_hold_the_same_rows()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var asCsv = CsvTable.Parse((await admin.GetAsync(Export, InWindow(("format", "csv")))).Text);
        var asJson = await ExportJsonAsync(admin);

        Assert.Equal(asCsv.Select(row => row["title"]), asJson.Select(row => (string?)row!["title"]));
    }

    [Fact]
    public async Task An_export_holds_every_row_the_filters_leave_and_no_others()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var events = await ExportJsonAsync(admin, ("source", "event"));
        var failed = await ExportJsonAsync(admin, ("source", "event"), ("level", "error"));

        Assert.Equal(Events, events.Count);
        Assert.Equal(new HashSet<string> { "event" }, events.Select(row => (string)row!["source"]!).ToHashSet());
        Assert.Equal(Enumerable.Range(0, Events).Count(index => index % 7 == 0), failed.Count);
        Assert.Equal(new HashSet<string> { "error" }, failed.Select(row => (string)row!["level"]!).ToHashSet());
    }

    public sealed class SeededExportFixture : SeededServerFixture
    {
        protected override void Seed(SqliteConnection connection)
        {
            SystemPartBHelpers.SeedUsers(connection);
            SeedSql.Execute(connection, "DELETE FROM jobs");
            SeedSql.Execute(connection, "BEGIN"); // one commit for all the rows, not one each
            for (var index = 0; index < Events; index++)
            {
                var result = index % 7 == 0 ? "failed" : "success";
                InsertEvent(connection, SeededAt.AddSeconds(index), "library.scan_completed", $"Event {index}", result);
            }

            for (var index = 0; index < Jobs; index++)
            {
                InsertJob(connection, SeededAt.AddSeconds(index), $"contract:export:{index}", "processing.file.remux_pass.v1", "pending");
            }

            SeedSql.Execute(connection, "COMMIT");
            AppendToServerLog(
                Server,
                Enumerable.Range(0, ServerLines).Select(index => LogLine(SeededAt.AddSeconds(index), "INFO", "weir.processing", $"Line {index}")));
        }
    }
}
