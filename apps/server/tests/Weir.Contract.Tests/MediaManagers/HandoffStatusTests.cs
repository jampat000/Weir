using System.Net;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.MediaManagers;

/// <summary>
/// Hand-off status for media managers: auth and the states a poll can read. Running work, cancelling and retention are in
/// <see cref="HandoffLifecycleTests"/>; folder hand-offs are in <see cref="HandoffFolderTests"/>.
/// </summary>
[ContractArea("media_managers")]
public sealed class HandoffStatusTests(HandoffServerFixture fixture) : IClassFixture<HandoffServerFixture>
{
    private const string Capabilities = $"{WeirClient.Api}/intake/capabilities";

    private WeirServer Server => fixture.Server;

    // --- auth --------------------------------------------------------------------------------------

    [Fact]
    public async Task No_configured_secret_is_a_403_that_says_what_to_set()
    {
        await using var fresh = await WeirServer.StartNewAsync(ManagerEnvironment.NoWebhookSecret);
        using var manager = fresh.CreateClient();

        foreach (var (method, path) in new[]
        {
            (HttpMethod.Get, Capabilities),
            (HttpMethod.Get, Handoffs.StatusPath("h1")),
            (HttpMethod.Delete, Handoffs.StatusPath("h1")),
        })
        {
            var response = await manager.RequestAsync(method, path);

            Assert.True(response.Status == HttpStatusCode.Forbidden, $"{method} {path}: {response}");
            Assert.Contains("Set a webhook secret", (string)response.Fields["detail"]!);
        }
    }

    [Fact]
    public async Task A_wrong_or_missing_secret_is_refused()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Handoffs.StatusResponseAsync(Server, "h1", new Dictionary<string, string>())).Status);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await Handoffs.StatusResponseAsync(Server, "h1", new Dictionary<string, string> { ["X-Webhook-Secret"] = "no" })).Status);
        using var manager = Server.CreateClient();
        var wrong = await manager.GetAsync(Capabilities, new Dictionary<string, string> { ["X-Webhook-Secret"] = "no" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.Status);
    }

    [Fact]
    public async Task Capabilities_name_every_ability()
    {
        using var manager = Server.CreateClient();

        var response = await manager.GetAsync(Capabilities, Handoffs.Secret);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var body = response.Fields;
        var capabilities = body["capabilities"]!.AsArray().Select(name => (string)name!);
        Assert.Equal(
            ["handoff-status", "handoff-cancel", "handoff-outcome", "handoff-outcome-codes", "library-folders"], capabilities);
        body.Remove("capabilities");
        Assert.Equal(["machine_name", "product", "version"], body.Select(field => field.Key));
        Assert.Equal("Weir", (string)body["product"]!);
    }

    [Fact]
    public async Task Capabilities_say_which_weir_version_answers()
    {
        using var manager = Server.CreateClient();

        var body = (await manager.GetAsync(Capabilities, Handoffs.Secret)).Fields;

        Assert.Matches(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.]+)?\z", (string)body["version"]!);
    }

    [Fact]
    public async Task Capabilities_publish_the_machine_name_for_a_manager_to_name_its_connection()
    {
        using var manager = Server.CreateClient();

        var response = await manager.GetAsync(Capabilities, Handoffs.Secret);

        var machineName = (string)response.Fields["machine_name"]!;
        // Windows truncates a long name to fifteen characters, so the machine's own name may be longer.
        Assert.NotEmpty(machineName);
        Assert.StartsWith(machineName, System.Net.Dns.GetHostName(), StringComparison.OrdinalIgnoreCase);
    }

    // --- status ------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_hand_off_is_404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await Handoffs.StatusResponseAsync(Server, "nope")).Status);

    [Fact]
    public async Task A_new_hand_off_is_queued_with_its_place()
    {
        // A server of its own, so no other test's queued work is ahead of this one.
        await using var fresh = await WeirServer.StartNewAsync(Handoffs.SecretEnvironment);
        using var admin = await fresh.CreateAdminClientAsync();
        using var temp = new TemporaryFolder();
        var folders = LibraryFolders.Make(Path.Combine(temp.Path, "movies"));
        await ProcessingLibraries.EnsureAsync(admin, "Movies", "movie", folders);

        await Handoffs.HandOffAsync(fresh, "h1", Path.Combine(folders.Watched, "Film", "film.mkv"));
        var body = await Handoffs.StatusAsync(fresh, "h1");

        Assert.Equal("h1", (string)body["handoffId"]!);
        Assert.Equal("queued", (string)body["state"]!);
        Assert.Equal(1, (int)body["queuePosition"]!);
        Assert.EndsWith("Z", (string)body["lastChangedUtc"]!);
    }

    [Fact]
    public async Task A_pending_retry_is_scheduled_for_when_it_will_run()
    {
        var movies = await fixture.MoviesAsync();
        var handoffId = Handoffs.NewId();
        await Handoffs.HandOffAsync(Server, handoffId, Path.Combine(movies.Watched, handoffId, "film.mkv"));
        var retryAt = DateTime.UtcNow + TimeSpan.FromMinutes(20);
        await Handoffs.SeedAsync(
            Server,
            handoffId,
            jobStatus: "completed",
            fileStatus: "processing_failed",
            file: new FileColumns(NextRetryAt: Handoffs.StoredTime(retryAt), StatusReason: "ffmpeg died."));

        var body = await Handoffs.StatusAsync(Server, handoffId);

        Assert.Equal("scheduled", (string)body["state"]!);
        Assert.StartsWith(Handoffs.MinutePrefix(retryAt), (string)body["scheduledFor"]!);
    }

    [Fact]
    public async Task A_retry_still_owed_after_its_backoff_is_scheduled_not_failed()
    {
        var movies = await fixture.MoviesAsync();
        var handoffId = Handoffs.NewId();
        await Handoffs.HandOffAsync(Server, handoffId, Path.Combine(movies.Watched, handoffId, "film.mkv"));
        var retryAt = DateTime.UtcNow - TimeSpan.FromMinutes(5);
        await Handoffs.SeedAsync(
            Server,
            handoffId,
            jobStatus: "completed",
            fileStatus: "processing_failed",
            file: new FileColumns(NextRetryAt: Handoffs.StoredTime(retryAt), FailureAttempts: 1, StatusReason: "ffmpeg died."));

        var body = await Handoffs.StatusAsync(Server, handoffId);

        Assert.Equal("scheduled", (string)body["state"]!);
        Assert.StartsWith(Handoffs.MinutePrefix(retryAt), (string)body["scheduledFor"]!);
    }

    /// <summary>
    /// Once the backoff has elapsed but no scan has picked the file up yet, the hand-off still reads <c>scheduled</c>. A retry is
    /// still coming (a scan can be up to five minutes away by default), and Deluno treats <c>failed</c> as final, so reporting
    /// <c>failed</c> would make it give up on a hand-off about to succeed.
    /// </summary>
    [Fact]
    public async Task An_overdue_retry_still_reads_scheduled_not_failed()
    {
        var movies = await fixture.MoviesAsync();
        var handoffId = Handoffs.NewId();
        await Handoffs.HandOffAsync(Server, handoffId, Path.Combine(movies.Watched, handoffId, "film.mkv"));
        var overdue = DateTime.UtcNow - TimeSpan.FromSeconds(5);
        await Handoffs.SeedAsync(
            Server,
            handoffId,
            jobStatus: "completed",
            fileStatus: "processing_failed",
            file: new FileColumns(NextRetryAt: Handoffs.StoredTime(overdue), StatusReason: "ffmpeg died."));

        var body = await Handoffs.StatusAsync(Server, handoffId);

        Assert.True(
            (string)body["state"]! == "scheduled",
            $"a retry that is merely overdue for its next scan is not a final failure: {body.ToJsonString()}");
    }

    [Fact]
    public async Task Waiting_for_the_manager_is_queued_not_stalled()
    {
        var movies = await fixture.MoviesAsync();
        var handoffId = Handoffs.NewId();
        await Handoffs.HandOffAsync(Server, handoffId, Path.Combine(movies.Watched, handoffId, "film.mkv"));
        await Handoffs.SeedAsync(
            Server,
            handoffId,
            jobStatus: "completed",
            fileStatus: "blocked_upstream",
            file: new FileColumns(StatusReason: "Deluno is still importing this file."));

        var body = await Handoffs.StatusAsync(Server, handoffId);

        Assert.Equal("queued", (string)body["state"]!);
        Assert.Equal("Deluno is still importing this file.", (string)body["message"]!);
    }

    [Theory]
    [InlineData("processed", "completed")]
    [InlineData("passed_through", "passed-through")]
    [InlineData("rejected", "rejected")]
    [InlineData("processing_failed", "failed")]
    [InlineData("out_of_schedule", "scheduled")]
    public async Task The_file_state_maps_to_the_agreed_vocabulary(string fileStatus, string state)
    {
        var movies = await fixture.MoviesAsync();
        var handoffId = Handoffs.NewId();
        await Handoffs.HandOffAsync(Server, handoffId, Path.Combine(movies.Watched, handoffId, "film.mkv"));
        await Handoffs.SeedAsync(Server, handoffId, jobStatus: "completed", fileStatus: fileStatus);

        Assert.Equal(state, (string)(await Handoffs.StatusAsync(Server, handoffId))["state"]!);
    }

    /// <summary>The reason the ledger exists. "Never heard of it" here would import the unprocessed original.</summary>
    [Fact]
    public async Task A_finished_hand_off_still_answers_after_job_rows_are_pruned()
    {
        var movies = await fixture.MoviesAsync();
        var handoffId = Handoffs.NewId();
        await Handoffs.HandOffAsync(Server, handoffId, Path.Combine(movies.Watched, handoffId, "film.mkv"));
        await Handoffs.SeedAsync(Server, handoffId, jobStatus: "completed", fileStatus: "processed");
        Assert.Equal("completed", (string)(await Handoffs.StatusAsync(Server, handoffId))["state"]!);

        // What job-row retention and "clear history" leave behind: no job row, no Files row.
        await using (var database = await Server.StopForDatabaseAsync())
        {
            Assert.Equal(
                1,
                SeedSql.Execute(database.Connection, "DELETE FROM jobs WHERE dedupe_key = $key", ("$key", RemuxJobs.HandoffDedupeKey(handoffId))));
            Assert.Equal(
                1,
                SeedSql.Execute(database.Connection, "DELETE FROM files WHERE relative_path = $path", ("$path", $"{handoffId}/film.mkv")));
        }

        Assert.Equal("completed", (string)(await Handoffs.StatusAsync(Server, handoffId))["state"]!);
    }

    /// <summary>Deluno reads an unchanged timestamp as "nothing happened". Polling must not reset it.</summary>
    [Fact]
    public async Task A_repeated_poll_does_not_move_last_changed()
    {
        var movies = await fixture.MoviesAsync();
        var handoffId = Handoffs.NewId();
        await Handoffs.HandOffAsync(Server, handoffId, Path.Combine(movies.Watched, handoffId, "film.mkv"));

        var first = (string)(await Handoffs.StatusAsync(Server, handoffId))["lastChangedUtc"]!;

        Assert.Equal(first, (string)(await Handoffs.StatusAsync(Server, handoffId))["lastChangedUtc"]!);
    }
}
