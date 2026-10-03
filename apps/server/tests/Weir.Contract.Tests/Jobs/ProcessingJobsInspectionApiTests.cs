using System.Net;
using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Jobs;

/// <summary>Job inspection (filters, operator guidance, what the default list hides) and cancelling a pending job.</summary>
[ContractArea("jobs")]
public sealed class ProcessingJobsInspectionApiTests(ServerFixture fixture) : IClassFixture<ServerFixture>
{
    private const string Sweep = JobRows.WorkTempStaleSweepKind;
    private const string Remux = JobsApi.RemuxPassKind;
    private const string Scan = JobsApi.ScanDispatchKind;
    private const string Inspection = $"{WeirClient.Api}/processing/jobs/inspection";

    /// <summary>
    /// Clears <c>jobs</c>, runs <paramref name="write"/>, and returns a signed-in client (the viewer when asked for).
    /// Rows are written while the server is stopped. A <c>leased</c> row written that way is recovered to <c>pending</c>
    /// at startup (a lease held across a restart belongs to a dead worker), so leased rows come from a live worker instead.
    /// </summary>
    private async Task<WeirClient> ReseedAsync(Action<SqliteConnection> write, string? username = null)
    {
        (await fixture.Server.CreateAdminClientAsync()).Dispose();
        await using (var database = await fixture.Server.StopForDatabaseAsync())
        {
            SeedSql.Execute(database.Connection, "DELETE FROM jobs");
            write(database.Connection);
            SeededAccounts.EnsureViewer(database.Connection);
        }

        var client = fixture.Server.CreateClient();
        if (username == SeededAccounts.ViewerUsername)
        {
            await client.LoginAsync(SeededAccounts.ViewerUsername, SeededAccounts.ViewerPassword);
        }
        else
        {
            await client.LoginAsync();
        }

        return client;
    }

    private static void SeedMixedStatusRows(SqliteConnection connection)
    {
        var now = DateTime.UtcNow;
        DateTime t0 = now.AddHours(-3), t1 = now.AddHours(-2), t2 = now.AddHours(-1);
        JobRows.Insert(connection, "rinsp-pending", Sweep, "pending", t0);
        JobRows.Insert(connection, "rinsp-done", Remux, "completed", t2, ("attempt_count", 1));
        JobRows.Insert(
            connection, "rinsp-fail", Scan, "failed", t1,
            ("attempt_count", 2), ("max_attempts", 2), ("last_error", "handler boom"));
        JobRows.Insert(
            connection, "rinsp-finalize", Remux, "handler_ok_finalize_failed", t2,
            ("attempt_count", 1), ("last_error", "finalize"));
        JobRows.Insert(connection, "rinsp-cancelled", Sweep, "cancelled", t1);
    }

    [Fact]
    public async Task Jobs_inspection_requires_auth()
    {
        using var client = fixture.Server.CreateClient();

        var response = await client.GetAsync(Inspection);

        JobsApi.Expect(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Jobs_inspection_default_includes_pending_and_leased()
    {
        using var client = await ReseedAsync(SeedMixedStatusRows);

        var response = await client.GetAsync($"{Inspection}?limit=20");

        JobsApi.Expect(response, HttpStatusCode.OK);
        var body = response.Fields;
        Assert.True((bool)body["default_recent_slice"]!);
        var jobs = body["jobs"]!.AsArray().Select(job => job!.AsObject()).ToList();
        Assert.Contains(Sweep, jobs.Select(job => (string)job["job_kind"]!).ToHashSet());
        var statuses = jobs.Select(job => (string)job["status"]!).ToHashSet();
        Assert.Contains("pending", statuses);
        Assert.Contains("cancelled", statuses);
        Assert.Contains("failed", statuses);
        Assert.Contains("handler_ok_finalize_failed", statuses);
        // Newest updated_at first.
        var stamps = jobs.Select(job => (string)job["updated_at"]!).ToList();
        Assert.Equal(stamps.OrderByDescending(stamp => stamp, StringComparer.Ordinal), stamps);
    }

    [Fact]
    public async Task Jobs_inspection_default_includes_a_leased_job()
    {
        await using var scenario = await LeasedJobScenario.StartAsync();

        var body = await JobsApi.InspectionAsync(scenario.Client, 20);

        Assert.True((bool)body["default_recent_slice"]!);
        var leased = body["jobs"]!.AsArray().Select(job => job!.AsObject()).Where(job => (int)job["id"]! == scenario.JobId).ToList();
        Assert.NotEmpty(leased);
        Assert.Equal("leased", (string)leased[0]["status"]!);
        Assert.False(string.IsNullOrEmpty((string?)leased[0]["lease_owner"]));
        Assert.NotNull(leased[0]["lease_expires_at"]);
    }

    [Fact]
    public async Task Jobs_inspection_returns_operator_guidance_and_keeps_lock_detail_secondary()
    {
        using var client = await ReseedAsync(connection => JobRows.Insert(
            connection, "operator-guidance-lock", Remux, "failed",
            columns:
            [
                ("last_error", "sqlite3.OperationalError: database is locked"),
                ("payload_json", "{\"relative_media_path\":\"Movie/Film.mkv\"}"),
            ]));

        var response = await client.GetAsync($"{Inspection}?limit=10");

        JobsApi.Expect(response, HttpStatusCode.OK);
        var job = response.Fields["jobs"]!.AsArray()
            .Select(item => item!.AsObject())
            .First(item => (string)item["dedupe_key"]! == "operator-guidance-lock");
        Assert.Contains("could not save the result", (string)job["operator_message"]!);
        Assert.Contains("Files at once", (string)job["next_action"]!);
        Assert.Equal("sqlite3.OperationalError: database is locked", (string)job["technical_detail"]!);
    }

    [Fact]
    public async Task Jobs_inspection_status_filter()
    {
        using var client = await ReseedAsync(SeedMixedStatusRows);

        var response = await client.GetAsync($"{Inspection}?status=pending&limit=10");

        JobsApi.Expect(response, HttpStatusCode.OK);
        var body = response.Fields;
        Assert.False((bool)body["default_recent_slice"]!);
        var jobs = body["jobs"]!.AsArray();
        Assert.NotEmpty(jobs);
        Assert.All(jobs, job => Assert.Equal("pending", (string)job!["status"]!));
    }

    [Fact]
    public async Task Jobs_default_hides_successful_periodic_scan_noise()
    {
        using var client = await ReseedAsync(connection =>
        {
            JobRows.Insert(connection, "routine-scan", Scan, "completed");
            JobRows.Insert(connection, "material-file-work", Remux, "completed");
        });

        var recent = await client.GetAsync($"{Inspection}?limit=20");
        var completed = await client.GetAsync($"{Inspection}?status=completed&limit=20");

        JobsApi.Expect(recent, HttpStatusCode.OK);
        Assert.Equal(
            ["material-file-work"],
            recent.Fields["jobs"]!.AsArray().Select(row => (string)row!["dedupe_key"]!).ToList());
        JobsApi.Expect(completed, HttpStatusCode.OK);
        Assert.True(
            new HashSet<string> { "routine-scan", "material-file-work" }
                .SetEquals(completed.Fields["jobs"]!.AsArray().Select(row => (string)row!["dedupe_key"]!)));
    }

    [Fact]
    public async Task Jobs_inspection_invalid_status_422()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var response = await admin.GetAsync($"{Inspection}?status=not_a_real_status");

        JobsApi.Expect(response, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Processing_job_cancel_pending_ok()
    {
        var jobId = 0;
        using var client = await ReseedAsync(connection => jobId = JobRows.Insert(connection, "to-cancel-contract", Sweep));

        var response = await client.PostWithCsrfAsync($"{WeirClient.Api}/processing/jobs/{jobId}/cancel-pending");

        JobsApi.Expect(response, HttpStatusCode.OK);
        Assert.True((bool)response.Fields["ok"]!);
        Assert.Equal("cancelled", (string)response.Fields["status"]!);
        var row = await JobsApi.JobByIdAsync(client, jobId);
        Assert.Equal("cancelled", (string)row["status"]!);
        Assert.Contains(":cancelled:", (string)row["dedupe_key"]!);
    }

    [Fact]
    public async Task Processing_job_cancel_pending_refuses_leased()
    {
        await using var scenario = await LeasedJobScenario.StartAsync();

        var response = await scenario.Client.PostWithCsrfAsync($"{WeirClient.Api}/processing/jobs/{scenario.JobId}/cancel-pending");

        JobsApi.Expect(response, HttpStatusCode.Conflict);
        Assert.Equal("leased", (string)(await JobsApi.JobByIdAsync(scenario.Client, scenario.JobId))["status"]!);
    }

    [Fact]
    public async Task Jobs_inspection_viewer_can_read()
    {
        using var client = await ReseedAsync(SeedMixedStatusRows, SeededAccounts.ViewerUsername);

        var response = await client.GetAsync($"{Inspection}?limit=5");

        JobsApi.Expect(response, HttpStatusCode.OK);
        Assert.True(response.Fields.ContainsKey("jobs"));
    }

    [Fact]
    public async Task Processing_job_cancel_pending_viewer_forbidden()
    {
        var jobId = 0;
        using var client = await ReseedAsync(
            connection => jobId = JobRows.Insert(connection, "viewer-deny", Sweep), SeededAccounts.ViewerUsername);

        var response = await client.PostWithCsrfAsync($"{WeirClient.Api}/processing/jobs/{jobId}/cancel-pending");

        JobsApi.Expect(response, HttpStatusCode.Forbidden);
    }
}
