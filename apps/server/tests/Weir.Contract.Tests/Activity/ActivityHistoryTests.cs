using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Activity;

/// <summary>Activity history: filtering, export, retention setting and removal.</summary>
[ContractArea("activity")]
public sealed class ActivityHistoryTests(ServerFixture fixture) : IClassFixture<ServerFixture>
{
    private const string Recent = $"{WeirClient.Api}/activity/recent";
    private const string FileHistory = $"{WeirClient.Api}/activity/file-history";

    private WeirServer Server => fixture.Server;

    [Fact]
    public async Task History_filters_on_why_how_where_and_which_file()
    {
        using var client = await SeededClientAsync();

        Assert.Equal(["Show processed"], await TitlesAsync(client, ("trigger", "webhook")));
        Assert.Equal(["Heat failed"], await TitlesAsync(client, ("result", "failed")));
        Assert.Equal(["Show processed"], await TitlesAsync(client, ("library_id", 2)));
        Assert.Equal(["Heat failed", "Heat was handed back"], await TitlesAsync(client, ("file", "heat.mkv")));
    }

    [Fact]
    public async Task The_page_is_told_how_far_back_history_goes()
    {
        using var client = await SeededClientAsync();

        var response = await client.GetAsync(Recent, ("module", "processing"));

        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        var body = response.Fields;
        Assert.Equal(ActivitySettings.DefaultRetentionDays, (int)body["retention_days"]!);
        Assert.NotNull(body["oldest_event_at"]);
        Assert.False(string.IsNullOrEmpty((string?)body["items"]![0]!["relative_path"]));
    }

    [Fact]
    public async Task A_filtered_range_exports_as_csv_and_json()
    {
        using var client = await SeededClientAsync();

        var asCsv = await client.GetAsync($"{WeirClient.Api}/activity/export", ("format", "csv"), ("file", "heat.mkv"));
        Assert.True(asCsv.Status == HttpStatusCode.OK, asCsv.ToString());
        Assert.Contains("attachment", asCsv.Header("Content-Disposition"));
        var rows = CsvTable.Parse(asCsv.Text);
        Assert.Equal(["Heat failed", "Heat was handed back"], rows.Select(row => row["title"]).Order(StringComparer.Ordinal));
        Assert.Equal(new HashSet<string> { "scheduled", "retry" }, rows.Select(row => row["trigger"]).ToHashSet());

        var asJson = await client.GetAsync(
            $"{WeirClient.Api}/activity/export", ("format", "json"), ("trigger", "manual"), ("module", "processing"));
        Assert.Equal(HttpStatusCode.OK, asJson.Status);
        Assert.Equal(["Alien processed"], asJson.Elements.Select(row => (string)row!["title"]!));
    }

    [Fact]
    public async Task Removing_one_files_history_says_what_goes_then_removes_only_that()
    {
        using var client = await SeededClientAsync();
        using var unrelatedFolder = new TemporaryFolder();
        var media = Path.Combine(unrelatedFolder.Path, "heat.mkv");
        await File.WriteAllBytesAsync(media, "not touched"u8.ToArray());

        var preview = await client.GetAsync(FileHistory, ("relative_path", "Heat/heat.mkv"), ("library_id", 1));
        Assert.True(preview.Status == HttpStatusCode.OK, preview.ToString());
        Assert.Equal((2, 1), ((int)preview.Fields["activity_events"]!, (int)preview.Fields["processing_records"]!));
        Assert.Contains("does not touch the file itself", (string)preview.Fields["message"]!);

        var removed = await client.PostWithCsrfAsync(
            $"{FileHistory}/remove", new JsonObject { ["relative_path"] = "Heat/heat.mkv", ["library_id"] = 1 });
        Assert.True(removed.Status == HttpStatusCode.OK, removed.ToString());
        Assert.Equal(
            (2, 1),
            ((int)removed.Fields["activity_events_deleted"]!, (int)removed.Fields["processing_records_deleted"]!));

        Assert.Equal(["Alien processed", "Show processed"], await TitlesAsync(client));
        await using (var database = await Server.StopForDatabaseAsync())
        {
            var remaining = SeedSql.Rows(database.Connection, "SELECT relative_path FROM file_logs ORDER BY id");
            Assert.Equal(["Alien/alien.mkv"], remaining.Select(row => (string)row["relative_path"]!));
        }

        Assert.Equal("not touched"u8.ToArray(), await File.ReadAllBytesAsync(media));
    }

    [Fact]
    public async Task Removing_history_needs_an_operator_and_a_fresh_token()
    {
        using var seeded = await SeededClientAsync();
        using var viewer = Server.CreateClient();
        await viewer.LoginAsync(ActivityRows.ViewerUsername, ActivityRows.ViewerPassword);

        var response = await viewer.PostWithCsrfAsync(
            $"{FileHistory}/remove", new JsonObject { ["relative_path"] = "Heat/heat.mkv" });

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
    }

    [Fact]
    public async Task Clearing_all_history_is_previewed_without_removing_anything()
    {
        using var client = await SeededClientAsync();

        var preview = await client.GetAsync($"{WeirClient.Api}/suite/operational-history/preview");

        Assert.True(preview.Status == HttpStatusCode.OK, preview.ToString());
        Assert.Equal("preview", (string)preview.Fields["status"]!);
        var everything = (int)(await client.GetAsync(Recent)).Fields["total"]!;
        Assert.Equal(everything, (int)preview.Fields["activity_events_deleted"]!);
        Assert.Equal(4, (await TitlesAsync(client)).Length);
    }

    [Fact]
    public async Task The_activity_horizon_is_a_saved_setting()
    {
        const int changedDays = 365;
        using var client = await Server.CreateAdminClientAsync();
        var current = await ActivitySettings.CurrentAsync(client);
        Assert.Equal(ActivitySettings.DefaultRetentionDays, (int)current["activity_retention_days"]!);

        try
        {
            var saved = await ActivitySettings.SaveAsync(client, ActivitySettings.UpdateBody(current, activityRetentionDays: changedDays));
            Assert.True(saved.Status == HttpStatusCode.OK, saved.ToString());
            Assert.Equal(changedDays, (int)saved.Fields["activity_retention_days"]!);
            Assert.Equal(changedDays, (int)(await ActivitySettings.CurrentAsync(client))["activity_retention_days"]!);
        }
        finally
        {
            var restored = await ActivitySettings.SaveAsync(
                client, ActivitySettings.UpdateBody(current, activityRetentionDays: ActivitySettings.DefaultRetentionDays));
            Assert.True(restored.Status == HttpStatusCode.OK, restored.ToString());
        }
    }

    /// <summary>
    /// The four Processing results and two processing records, with a fresh admin client. The admin exists
    /// before seeding, because seeding replaces all history and restarts the server on a new port.
    /// </summary>
    private async Task<WeirClient> SeededClientAsync()
    {
        using (await Server.CreateAdminClientAsync())
        {
        }

        await using (var database = await Server.StopForDatabaseAsync())
        {
            ActivityRows.SeedHistory(database.Connection);
        }

        var client = Server.CreateClient();
        await client.LoginAsync();
        return client;
    }

    /// <summary>The Processing titles, sorted. Signing in records its own event; these tests are about the history they seeded.</summary>
    private static async Task<string[]> TitlesAsync(WeirClient client, params (string Name, object Value)[] filters)
    {
        var response = await client.GetAsync(Recent, [("module", "processing"), .. filters]);
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        return response.Fields["items"]!.AsArray().Select(item => (string)item!["title"]!).Order(StringComparer.Ordinal).ToArray();
    }
}
