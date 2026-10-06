using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Libraries.LibrariesPartAHelpers;

namespace Weir.Contract.Tests.Libraries;

/// <summary>Processing overview stats: access, shape, and remux savings counted only from finished successes.</summary>
[ContractArea("libraries")]
public sealed class ProcessingOverviewStatsApiTests(ServerFixture fixture) : IClassFixture<ServerFixture>
{
    private const string StatsPath = Api + "/processing/overview-stats";

    private static void RemuxEvent(SqliteConnection connection, string title, JsonObject detail) =>
        InsertActivityEvent(connection, RemuxPassCompletedEvent, "processing", title, detail.ToJsonString());

    private static string WriteOutputFile(WeirServer server, string name)
    {
        var path = Path.Combine(server.Home, name);
        File.WriteAllBytes(path, "ok"u8.ToArray());
        return path;
    }

    [Fact]
    public async Task Processing_overview_stats_requires_auth()
    {
        using var client = fixture.Server.CreateClient();

        (await client.GetAsync(StatsPath)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Processing_overview_stats_shape()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var response = await admin.GetAsync(StatsPath);

        response.ShouldBe(HttpStatusCode.OK);
        var body = response.Fields;
        Assert.Equal(30, (int)body["window_days"]!);
        Assert.True(body.ContainsKey("files_processed"));
        Assert.True(body.ContainsKey("files_failed"));
        Assert.True(IsInteger(body["files_failed"]));
        Assert.True(body.ContainsKey("success_rate_percent"));
        Assert.Equal(0, (int)body["output_written_count"]!);
        Assert.Equal(0, (int)body["already_optimized_count"]!);
        Assert.Equal(0, (long)body["net_space_saved_bytes"]!);
        Assert.Equal(0.0, (double)body["net_space_saved_percent"]!);
    }

    [Fact]
    public async Task Processing_overview_stats_aggregates_remux_savings()
    {
        await using var server = await WeirServer.StartNewAsync();
        var out1 = WriteOutputFile(server, "stats-out-1.mkv");
        var out2 = WriteOutputFile(server, "stats-out-2.mkv");
        var unchanged = WriteOutputFile(server, "stats-unchanged.mkv");
        await using (var database = await server.StopForDatabaseAsync())
        {
            SeedSql.Execute(database.Connection, "DELETE FROM activity_events");
            RemuxEvent(
                database.Connection,
                "Remux one",
                Obj(("outcome", "live_output_written"), ("source_size_bytes", 1_000), ("output_size_bytes", 700), ("output_file", out1)));
            RemuxEvent(
                database.Connection,
                "Remux two",
                Obj(("outcome", "live_output_written"), ("source_size_bytes", 2_000), ("output_size_bytes", 1_200), ("output_file", out2)));
            RemuxEvent(
                database.Connection,
                "Already optimized",
                Obj(("outcome", "live_skipped_not_required"), ("output_copied_without_remux", true), ("output_file", unchanged)));
        }

        using var admin = await server.CreateAdminClientAsync();
        var response = await admin.GetAsync(StatsPath);
        response.ShouldBe(HttpStatusCode.OK);
        var body = response.Fields;
        Assert.Equal(2, (int)body["output_written_count"]!);
        Assert.Equal(1, (int)body["already_optimized_count"]!);
        Assert.Equal(3, (int)body["files_processed"]!);
        Assert.Equal(1_100, (long)body["net_space_saved_bytes"]!);
        Assert.Equal(36.7, (double)body["net_space_saved_percent"]!);
    }

    [Fact]
    public async Task Processing_overview_stats_excludes_non_finalized_successes()
    {
        await using var server = await WeirServer.StartNewAsync();
        var finalized = WriteOutputFile(server, "stats-finalized.mkv");
        var missing = Path.Combine(server.Home, "stats-missing.mkv");
        await using (var database = await server.StopForDatabaseAsync())
        {
            SeedSql.Execute(database.Connection, "DELETE FROM activity_events");
            SeedSql.Execute(database.Connection, "DELETE FROM jobs");
            InsertJob(
                database.Connection,
                "completed-job-without-finalized-activity",
                status: "completed",
                payload: Obj(("relative_media_path", "SeenOnly.mkv")));
            InsertJob(database.Connection, "failed-job", status: "failed", payload: Obj(("relative_media_path", "Failed.mkv")));
            RemuxEvent(
                database.Connection,
                "Finalized",
                Obj(("outcome", "live_output_written"), ("source_size_bytes", 100), ("output_size_bytes", 80), ("output_file", finalized)));
            RemuxEvent(
                database.Connection,
                "Missing output",
                Obj(("outcome", "live_output_written"), ("source_size_bytes", 100), ("output_size_bytes", 80), ("output_file", missing)));
            RemuxEvent(
                database.Connection,
                "No-change detected only",
                Obj(("outcome", "live_skipped_not_required"), ("output_copied_without_remux", false), ("output_file", finalized)));
        }

        using var admin = await server.CreateAdminClientAsync();
        var response = await admin.GetAsync(StatsPath);
        response.ShouldBe(HttpStatusCode.OK);
        var body = response.Fields;
        Assert.Equal(2, (int)body["files_processed"]!);
        Assert.Equal(2, (int)body["output_written_count"]!);
        Assert.Equal(0, (int)body["already_optimized_count"]!);
        Assert.Equal(1, (int)body["files_failed"]!);
    }
}
