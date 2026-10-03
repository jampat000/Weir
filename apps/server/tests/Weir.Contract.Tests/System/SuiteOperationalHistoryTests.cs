using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>Resetting operational history: it needs confirmation, and clears history without touching active work.</summary>
[ContractArea("system")]
public sealed class SuiteOperationalHistoryTests(ServerFixture fixture) : IClassFixture<ServerFixture>
{
    private const string Reset = SystemPartBHelpers.Api + "/suite/operational-history/reset";
    private const string RemuxPass = "processing.file.remux_pass.v1";

    private WeirServer Server => fixture.Server;

    [Fact]
    public async Task Operational_history_reset_requires_confirmation()
    {
        using var admin = await Server.CreateAdminClientAsync();

        var response = await admin.PostWithCsrfAsync(Reset, new JsonObject { ["confirm"] = "wrong" });

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Contains("RESET", (string)response.Fields["detail"]!);
    }

    [Fact]
    public async Task Operational_history_reset_clears_history_but_keeps_active_work()
    {
        (await Server.CreateAdminClientAsync()).Dispose(); // makes the first admin before the database is seeded

        await using (var database = await Server.StopForDatabaseAsync())
        {
            var connection = database.Connection;
            SeedSql.Execute(connection, "DELETE FROM activity_events");
            SeedSql.Execute(connection, "DELETE FROM jobs");
            SeedSql.Execute(
                connection,
                "INSERT INTO activity_events (event_type, module, title, detail, created_at) VALUES ($type, $module, $title, $detail, $at)",
                ("$type", "processing.file_remux_pass_completed"),
                ("$module", "processing"),
                ("$title", "Finished file"),
                ("$detail", new JsonObject { ["outcome"] = "live_output_written" }.ToJsonString()),
                ("$at", SeedSql.UtcText(DateTime.UtcNow)));
            var now = SeedSql.UtcText(DateTime.UtcNow);
            foreach (var (key, status) in new[] { ("processing-done", "completed"), ("processing-pending", "pending") })
            {
                SeedSql.Execute(
                    connection,
                    "INSERT INTO jobs (dedupe_key, job_kind, status, created_at, updated_at) VALUES ($key, $kind, $status, $now, $now)",
                    ("$key", key),
                    ("$kind", RemuxPass),
                    ("$status", status),
                    ("$now", now));
            }
        }

        using var admin = await Server.CreateAdminClientAsync();
        var response = await admin.PostWithCsrfAsync(Reset, new JsonObject { ["confirm"] = "RESET" });
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        var body = response.Fields;
        Assert.Equal("reset", (string?)body["status"]);
        Assert.True((long)body["activity_events_deleted"]! >= 1);
        Assert.Equal(1, (long)body["jobs_deleted"]!);

        await using var after = await Server.StopForDatabaseAsync();
        Assert.Equal(0L, Convert.ToInt64(SeedSql.Scalar(after.Connection, "SELECT COUNT(*) FROM activity_events"), System.Globalization.CultureInfo.InvariantCulture));
        var keys = SeedSql.Rows(after.Connection, "SELECT dedupe_key FROM jobs").Select(row => (string)row["dedupe_key"]!).ToHashSet();
        Assert.DoesNotContain("processing-done", keys);
        Assert.Contains("processing-pending", keys);
    }
}
