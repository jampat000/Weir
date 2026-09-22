using System.Net;

namespace Weir.Api.Tests.Platform;

/// <summary>
/// #633 over real HTTP: "Files at once" goes to 10, a library can follow it, the resolution budget is a switch, and
/// <c>GET /processing/files-at-once</c> says what waiting files are waiting for.
/// </summary>
public sealed class ProcessingFilesAtOnceApiTests
{
    private const string Remux = "processing.file.remux_pass.v1";

    private static async Task<(WeirTestServer Server, ApiTestClient Client)> SignedInAsync()
    {
        var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        return (server, client);
    }

    [Fact]
    public async Task Files_at_once_saves_up_to_ten_and_refuses_eleven()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        using var ten = await client.PutAsync("/api/v1/processing/operator-settings", new { csrf_token = await client.CsrfAsync(), max_concurrent_files = 10 });
        Assert.Equal(HttpStatusCode.OK, ten.StatusCode);
        Assert.Equal(10, (await ApiTestClient.Json(ten))["max_concurrent_files"]!.GetValue<int>());

        using var eleven = await client.PutAsync("/api/v1/processing/operator-settings", new { csrf_token = await client.CsrfAsync(), max_concurrent_files = 11 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, eleven.StatusCode);
    }

    [Fact]
    public async Task The_resolution_budget_is_off_until_it_is_switched_on()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        using var before = await client.GetAsync("/api/v1/processing/operator-settings");
        Assert.False((await ApiTestClient.Json(before))["runner_budget_enabled"]!.GetValue<bool>());

        using var saved = await client.PutAsync("/api/v1/processing/operator-settings", new { csrf_token = await client.CsrfAsync(), runner_budget_enabled = true });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.True((await ApiTestClient.Json(saved))["runner_budget_enabled"]!.GetValue<bool>());
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT runner_budget_enabled FROM operator_settings WHERE id = 1"));
    }

    [Fact]
    public async Task A_new_library_follows_files_at_once_and_can_be_held_lower()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        using var created = await client.PostAsync(
            "/api/v1/processing/libraries",
            new { csrf_token = await client.CsrfAsync(), name = "Anime", media_type = "movie", watched_folder = @"c:\anime-in", output_folder = @"c:\anime-out" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(0, (await ApiTestClient.Json(created))["max_concurrent_files"]!.GetValue<int>());

        using var held = await client.PostAsync(
            "/api/v1/processing/libraries",
            new { csrf_token = await client.CsrfAsync(), name = "Kids", media_type = "movie", watched_folder = @"c:\kids-in", output_folder = @"c:\kids-out", max_concurrent_files = 2 });
        Assert.Equal(2, (await ApiTestClient.Json(held))["max_concurrent_files"]!.GetValue<int>());

        using var tooMany = await client.PostAsync(
            "/api/v1/processing/libraries",
            new { csrf_token = await client.CsrfAsync(), name = "More", media_type = "movie", watched_folder = @"c:\more-in", output_folder = @"c:\more-out", max_concurrent_files = 11 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMany.StatusCode);
    }

    [Fact]
    public async Task The_read_out_says_nothing_is_waiting_on_a_quiet_server()
    {
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;

        using var response = await client.GetAsync("/api/v1/processing/files-at-once");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ApiTestClient.Json(response);
        Assert.Equal(
            ["files_at_once", "worker_slots", "effective_files_at_once", "running", "waiting", "waiting_for", "message", "slots_note"],
            body.AsObject().Select(pair => pair.Key));
        Assert.Equal((1, 0, "nothing", string.Empty), (body["files_at_once"]!.GetValue<int>(), body["waiting"]!.GetValue<int>(), body["waiting_for"]!.GetValue<string>(), body["message"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Issue_636_the_read_out_answers_while_something_else_holds_the_write_lock()
    {
        // It read through the job queue's write transaction, so with several files being processed it queued behind
        // the workers' writes and failed with "database is locked" after the busy timeout.
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;
        using var writer = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={TestDatabase.PathFor(server)};Pooling=False");
        writer.Open();
        using (var begin = writer.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE; UPDATE operator_settings SET max_concurrent_files = 4;";
            begin.ExecuteNonQuery();
        }

        var started = System.Diagnostics.Stopwatch.StartNew();
        using var response = await client.GetAsync("/api/v1/processing/files-at-once");
        started.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The uncommitted 4 is not visible; the read saw the last committed value and did not wait for the writer.
        Assert.Equal(1, (await ApiTestClient.Json(response))["files_at_once"]!.GetValue<int>());
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(10), $"The read-out waited {started.Elapsed.TotalSeconds:0.0}s for the write lock.");

        using var rollback = writer.CreateCommand();
        rollback.CommandText = "ROLLBACK";
        rollback.ExecuteNonQuery();
    }

    [Fact]
    public async Task The_read_out_counts_what_is_running_and_what_is_due_and_says_why_it_waits()
    {
        // The API test server starts with its workers off, so the queued rows stay exactly as seeded - and that is the
        // reason the read-out gives. Which limit is named when workers are on is covered by FilesAtOnceTests.
        var (server, client) = await SignedInAsync();
        await using var disposeServer = server;
        var library = await TestDatabase.ScalarAsync(server, "SELECT id FROM libraries ORDER BY id LIMIT 1");
        await TestDatabase.ExecuteAsync(server, $"UPDATE libraries SET max_concurrent_files = 1, name = 'Movies' WHERE id = {library}");
        await TestDatabase.ExecuteAsync(server, "UPDATE operator_settings SET max_concurrent_files = 3");
        var payload = $"{{\"library_id\": {library}, \"relative_media_path\": \"a.mkv\"}}";
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status, lease_owner, lease_expires_at) VALUES ('running', @kind, @payload, 'leased', 'w', '2099-01-01 00:00:00.000000')",
            ("@kind", Remux), ("@payload", payload));
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status) VALUES ('waiting', @kind, @payload, 'pending')",
            ("@kind", Remux), ("@payload", payload));
        // Held back until later (a retry's backoff, a file waiting out its minimum age): waiting on no limit.
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status, not_before) VALUES ('later', @kind, @payload, 'pending', '2099-01-01 00:00:00.000000')",
            ("@kind", Remux), ("@payload", payload));

        using var response = await client.GetAsync("/api/v1/processing/files-at-once");

        var body = await ApiTestClient.Json(response);
        Assert.Equal((1, 1, "workers_off"), (body["running"]!.GetValue<int>(), body["waiting"]!.GetValue<int>(), body["waiting_for"]!.GetValue<string>()));
        Assert.Equal((3, 0, 0), (body["files_at_once"]!.GetValue<int>(), body["worker_slots"]!.GetValue<int>(), body["effective_files_at_once"]!.GetValue<int>()));
        Assert.Contains("workers are switched off", body["message"]!.GetValue<string>(), StringComparison.Ordinal);
    }
}
