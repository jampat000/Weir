using System.Net;
using Weir.Api.Tests.Platform;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.MediaManagers;

/// <summary>
/// #652 over HTTP: Sonarr's and Radarr's import webhook, and the hand-off outcome Deluno sends, record what became of a
/// file Weir handed back, and release Weir's copy only while it is exactly the file Weir wrote. Simulated media only.
/// </summary>
public sealed class HandbackOutcomeApiTests : IDisposable
{
    private const string WebhookSecret = "s3cret";

    private static readonly Dictionary<string, string> SecretHeader = new() { ["X-Webhook-Secret"] = WebhookSecret };

    private readonly string _root = Path.Join(Path.GetTempPath(), "weir-handback-" + Guid.NewGuid().ToString("N"));

    public HandbackOutcomeApiTests()
    {
        Directory.CreateDirectory(Watched);
        Directory.CreateDirectory(Output);
    }

    private string Watched => Path.Join(_root, "downloads");

    private string Output => Path.Join(_root, "hand-back");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static Task<WeirTestServer> StartAsync(string webhookSecret = WebhookSecret) =>
        WeirTestServer.StartAsync([("WEIR_SESSION_SECRET", Secret), ("WEIR_PROCESSING_WORKER_COUNT", "0"), ("WEIR_MEDIA_MANAGER_WEBHOOK_SECRET", webhookSecret)]);

    /// <summary>The seeded Movies library, pointed at this test's folders.</summary>
    private async Task<long> MoviesAsync(WeirTestServer server)
    {
        await TestDatabase.ExecuteAsync(
            server, "UPDATE libraries SET watched_folder = $w, output_folder = $o WHERE media_type = 'movie'", ("$w", Watched), ("$o", Output));
        return await TestDatabase.ScalarAsync(server, "SELECT id FROM libraries WHERE media_type = 'movie' ORDER BY id LIMIT 1");
    }

    /// <summary>A processed file whose cleaned copy Weir wrote into the output folder, recorded as a pass records it.</summary>
    private async Task<string> HandedBackAsync(WeirTestServer server, long library, string relative)
    {
        var source = Path.Join(Watched, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await File.WriteAllTextAsync(source, "the original download");
        var copy = Path.Join(Output, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        await File.WriteAllTextAsync(copy, "the cleaned copy");
        var info = new FileInfo(copy);
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO files (library_id, relative_path, status, status_reason) VALUES ($l, $p, 'processed', 'Finished processing this file.') " +
            "ON CONFLICT (library_id, relative_path) DO UPDATE SET status = 'processed'",
            ("$l", library),
            ("$p", relative));
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO handbacks (library_id, relative_path, output_path, output_size, output_mtime_ns, written_at) VALUES ($l, $p, $o, $s, $m, '2026-09-20 10:00:00.000000')",
            ("$l", library),
            ("$p", relative),
            ("$o", copy),
            ("$s", info.Length),
            ("$m", (info.LastWriteTimeUtc - DateTime.UnixEpoch).Ticks * 100));
        return copy;
    }

    private static object RadarrImport(string sourcePath, string? downloadId = null) => new
    {
        eventType = "Download",
        movie = new { id = 7, title = "The Long Tide", year = 2024 },
        movieFile = new { id = 12, path = "/movies/The Long Tide (2024)/The Long Tide (2024).mkv", sourcePath },
        downloadClient = "qBittorrent",
        downloadId = downloadId ?? "A1B2C3",
    };

    // --- Sonarr and Radarr ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_radarr_import_of_a_handed_back_copy_is_recorded_and_the_exact_copy_is_released()
    {
        await using var server = await StartAsync();
        await TestDatabase.SeedAdminAsync(server);
        var library = await MoviesAsync(server);
        var copy = await HandedBackAsync(server, library, "The.Long.Tide.2024/The.Long.Tide.2024.mkv");
        var manager = new ApiTestClient(server);

        // Radarr sees Weir's output folder under its own name (a remote path mapping), so only the end of the path matches.
        using var response = await manager.PostAsync(
            "/api/v1/intake/webhook/radarr", RadarrImport("/mnt/weir-out/The.Long.Tide.2024/The.Long.Tide.2024.mkv"), SecretHeader);

        Assert.Equal(
            """{"status":"ok","source":"radarr","event":"imported","matched":true,"released":true,"message":"Weir removed its copy from the hand-back folder, because Radarr has the file now."}""",
            await response.Content.ReadAsStringAsync());
        Assert.False(File.Exists(copy));
        Assert.True(File.Exists(Path.Join(Watched, "The.Long.Tide.2024", "The.Long.Tide.2024.mkv")));
        Assert.Equal(1, await TestDatabase.ScalarAsync(
            server,
            "SELECT count(*) FROM handbacks WHERE outcome = 'imported' AND outcome_by = 'Radarr' AND released_at IS NOT NULL " +
            "AND imported_path = '/movies/The Long Tide (2024)/The Long Tide (2024).mkv'"));
        Assert.Equal(1, await TestDatabase.ScalarAsync(
            server, "SELECT count(*) FROM activity_events WHERE event_type = 'processing.handback_outcome' AND title = 'Radarr imported The.Long.Tide.2024.mkv'"));

        // History shows it.
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        using var files = await client.GetAsync("/api/v1/processing/files");
        var handback = (await Json(files))["files"]!.AsArray().Single()!["handback"]!;
        Assert.Equal(("imported", "Radarr"), (handback["outcome"]!.GetValue<string>(), handback["outcome_by"]!.GetValue<string>()));
        Assert.NotNull(handback["released_at"]!.GetValue<string>());

        // Radarr sending the same message again changes nothing.
        using var again = await manager.PostAsync(
            "/api/v1/intake/webhook/radarr", RadarrImport("/mnt/weir-out/The.Long.Tide.2024/The.Long.Tide.2024.mkv"), SecretHeader);
        Assert.Contains("\"released\":true", await again.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM activity_events WHERE event_type = 'processing.handback_outcome'"));
    }

    [Fact]
    public async Task A_copy_that_changed_since_Weir_wrote_it_is_recorded_but_kept()
    {
        await using var server = await StartAsync();
        var library = await MoviesAsync(server);
        var copy = await HandedBackAsync(server, library, "Film/film.mkv");
        await File.AppendAllTextAsync(copy, ", and then someone else wrote to it");

        using var response = await new ApiTestClient(server).PostAsync("/api/v1/intake/webhook/radarr", RadarrImport(copy), SecretHeader);

        Assert.Contains("\"matched\":true,\"released\":false", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.True(File.Exists(copy));
        Assert.Equal("Weir's copy has changed since Weir wrote it, so Weir left it alone.", await TestDatabase.ScalarStringAsync(server, "SELECT release_note FROM handbacks"));
    }

    [Fact]
    public async Task A_moved_copy_records_the_import_and_removes_nothing()
    {
        await using var server = await StartAsync();
        var library = await MoviesAsync(server);
        var copy = await HandedBackAsync(server, library, "Film/film.mkv");
        var neighbour = Path.Join(Output, "Film", "film.nfo");
        await File.WriteAllTextAsync(neighbour, "a sidecar Weir never touches");
        File.Delete(copy);

        using var response = await new ApiTestClient(server).PostAsync("/api/v1/intake/webhook/radarr", RadarrImport(copy), SecretHeader);

        Assert.Equal(
            """{"status":"ok","source":"radarr","event":"imported","matched":true,"released":false,"message":"Radarr moved Weir's copy into its library, so there was nothing for Weir to remove."}""",
            await response.Content.ReadAsStringAsync());
        Assert.True(File.Exists(neighbour));
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM handbacks WHERE outcome = 'imported' AND released_at IS NULL AND settled_at IS NOT NULL"));
    }

    [Fact]
    public async Task A_sonarr_import_of_a_file_Weir_never_handed_back_changes_nothing()
    {
        await using var server = await StartAsync();
        var library = await MoviesAsync(server);
        var copy = await HandedBackAsync(server, library, "Film/film.mkv");
        var sonarr = new
        {
            eventType = "Download",
            series = new { id = 3, title = "Paper Lanterns" },
            episodes = new[] { new { id = 41, seasonNumber = 1, episodeNumber = 2, title = "The Second" } },
            episodeFile = new { path = "/tv/Paper Lanterns/Season 01/Paper Lanterns - S01E02.mkv", sourcePath = "/downloads/Paper.Lanterns.S01E02/Paper.Lanterns.S01E02.mkv" },
            downloadId = "NOT-WEIRS",
        };

        using var response = await new ApiTestClient(server).PostAsync("/api/v1/intake/webhook/sonarr", sonarr, SecretHeader);

        Assert.Equal("""{"status":"ignored","source":"sonarr","event":"imported"}""", await response.Content.ReadAsStringAsync());
        Assert.True(File.Exists(copy));
        Assert.Equal(0, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM handbacks WHERE outcome IS NOT NULL OR settled_at IS NOT NULL"));
        Assert.Equal(0, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM activity_events WHERE event_type = 'processing.handback_outcome'"));
    }

    [Fact]
    public async Task An_import_with_no_webhook_secret_anywhere_is_recorded_but_never_removes_a_file()
    {
        await using var server = await StartAsync(webhookSecret: string.Empty);
        var library = await MoviesAsync(server);
        var copy = await HandedBackAsync(server, library, "Film/film.mkv");

        using var response = await new ApiTestClient(server).PostAsync("/api/v1/intake/webhook/radarr", RadarrImport(copy));

        Assert.Contains("\"matched\":true,\"released\":false", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.True(File.Exists(copy));
        Assert.StartsWith("Weir kept its copy, because Radarr's messages to Weir carry no webhook secret", await TestDatabase.ScalarStringAsync(server, "SELECT release_note FROM handbacks"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_import_is_matched_by_the_download_id_a_hand_off_recorded()
    {
        await using var server = await StartAsync();
        var library = await MoviesAsync(server);
        var copy = await HandedBackAsync(server, library, "Film/film.mkv");
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO media_manager_handoffs (source_key, handoff_id, library_id, relative_path, state, download_id) VALUES ('native', 'n1', $l, 'Film/film.mkv', 'completed', 'DL-42')",
            ("$l", library));

        using var response = await new ApiTestClient(server).PostAsync(
            "/api/v1/intake/webhook/radarr", RadarrImport("/somewhere/else/renamed.mkv", downloadId: "DL-42"), SecretHeader);

        Assert.Contains("\"matched\":true,\"released\":true", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(File.Exists(copy));
    }

    // --- the hand-off outcome ------------------------------------------------------------------------------------------

    /// <summary>A Deluno hand-off Weir received and finished, with the copy it handed back.</summary>
    private async Task<string> FinishedHandoffAsync(WeirTestServer server, string handoffId = "h1")
    {
        var library = await MoviesAsync(server);
        var source = Path.Join(Watched, "Film", "film.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await File.WriteAllTextAsync(source, "the original download");
        var handoff = new { eventType = "deluno.processor-handoff", handoffId, libraryId = "lib-1", mediaType = "movies", sourcePath = source, callbackPath = "/api/integrations/processors/events" };
        using (var queued = await new ApiTestClient(server).PostAsync("/api/v1/intake/webhook/deluno", handoff, SecretHeader))
        {
            Assert.Equal(HttpStatusCode.OK, queued.StatusCode);
        }

        await TestDatabase.ExecuteAsync(server, "UPDATE jobs SET status = 'completed' WHERE job_kind = 'processing.file.remux_pass.v1'");
        return await HandedBackAsync(server, library, "Film/film.mkv");
    }

    /// <summary>Exactly what Deluno's <c>ReportOutcomeAsync</c> sends: <c>JsonContent.Create</c>, web defaults, nulls kept.</summary>
    private static StringContent DelunoOutcome(string outcome, string? importedPath, string? reason) =>
        TestDatabase.RawJson(System.Text.Json.JsonSerializer.Serialize(
            new { outcome, occurredUtc = new DateTimeOffset(2026, 9, 23, 10, 11, 12, TimeSpan.Zero).AddTicks(1234567), importedPath, reason },
            DelunoJson));

    private static readonly System.Text.Json.JsonSerializerOptions DelunoJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static Task<HttpResponseMessage> PostOutcomeAsync(WeirTestServer server, string handoffId, HttpContent content, bool withSecret = true) =>
        new ApiTestClient(server).SendAsync(
            HttpMethod.Post, $"/api/v1/intake/handoffs/deluno/{handoffId}/outcome", headers: withSecret ? SecretHeader : null, content: content);

    [Fact]
    public async Task Imported_answers_200_and_releases_the_exact_copy_and_the_same_outcome_again_answers_the_same()
    {
        await using var server = await StartAsync();
        var copy = await FinishedHandoffAsync(server);
        const string expected =
            """{"handoffId":"h1","outcome":"imported","released":true,"message":"Weir recorded that Deluno imported the file and released its copy."}""";

        using (var response = await PostOutcomeAsync(server, "h1", DelunoOutcome("imported", "/media/movies/Film (2020)/Film (2020).mkv", null)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(expected, await response.Content.ReadAsStringAsync());
        }

        Assert.False(File.Exists(copy));
        Assert.True(File.Exists(Path.Join(Watched, "Film", "film.mkv")));
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM media_manager_handoffs WHERE outcome = 'imported' AND outcome_released = 1"));
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM handbacks WHERE outcome_by = 'Deluno' AND imported_path = '/media/movies/Film (2020)/Film (2020).mkv'"));

        using (var repeat = await PostOutcomeAsync(server, "h1", DelunoOutcome("imported", "/media/movies/Film (2020)/Film (2020).mkv", null)))
        {
            Assert.Equal((HttpStatusCode.OK, expected), (repeat.StatusCode, await repeat.Content.ReadAsStringAsync()));
        }

        using var different = await PostOutcomeAsync(server, "h1", DelunoOutcome("not-imported", null, "Changed its mind."));
        Assert.Equal(
            (HttpStatusCode.Conflict, "Deluno already said it imported this file, so Weir kept that answer."),
            (different.StatusCode, await Detail(different)));
    }

    [Fact]
    public async Task Not_imported_is_final_records_the_reason_and_keeps_the_copy()
    {
        await using var server = await StartAsync();
        var copy = await FinishedHandoffAsync(server);

        using var response = await PostOutcomeAsync(server, "h1", DelunoOutcome("not-imported", null, "The release is a sample."));

        Assert.Equal(
            """{"handoffId":"h1","outcome":"not-imported","released":false,"message":"Weir recorded that the file will not be imported, and kept its copy."}""",
            await response.Content.ReadAsStringAsync());
        Assert.True(File.Exists(copy));
        Assert.Equal(
            "Deluno will not import this file: The release is a sample. Weir kept its copy in the hand-back folder.",
            await TestDatabase.ScalarStringAsync(server, "SELECT release_note FROM handbacks WHERE outcome = 'not-imported' AND outcome_reason = 'The release is a sample.'"));
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM activity_events WHERE title = 'Deluno will not import film.mkv'"));
    }

    [Fact]
    public async Task A_moved_copy_is_imported_without_removing_anything()
    {
        await using var server = await StartAsync();
        var copy = await FinishedHandoffAsync(server);
        File.Delete(copy);

        using var response = await PostOutcomeAsync(server, "h1", DelunoOutcome("imported", "/media/movies/Film/film.mkv", null));

        Assert.Equal(
            """{"handoffId":"h1","outcome":"imported","released":false,"message":"Weir recorded that Deluno imported the file. Deluno moved Weir's copy into its library, so there was nothing for Weir to remove."}""",
            await response.Content.ReadAsStringAsync());
    }

    /// <summary>A pass-through job for the film that ran out of retries, queued at <paramref name="createdAt"/>.</summary>
    private static async Task FailedPassThroughAsync(WeirTestServer server, string createdAt)
    {
        var library = await TestDatabase.ScalarAsync(server, "SELECT id FROM libraries WHERE media_type = 'movie' ORDER BY id LIMIT 1");
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status, last_error, created_at, updated_at) " +
            "VALUES ($key, 'processing.file.pass_through.v1', '{}', 'failed', 'Deluno stopped waiting for this file.', $at, $at)",
            ("$key", $"processing.file.pass_through.v1:{library}:Film/film.mkv:old-fingerprint"),
            ("$at", createdAt));
    }

    [Fact]
    public async Task A_failed_job_left_by_an_earlier_hand_off_of_the_same_file_does_not_fail_this_one()
    {
        // An earlier hand-off of the same file left a pass-through that ran out of retries. That job is found by the
        // file's path, so without a guard the new hand-off, which Weir has just completed, reads "failed" and Weir
        // refuses Deluno's "imported" with 409.
        await using var server = await StartAsync();
        await FailedPassThroughAsync(server, "2026-09-22 09:00:00.000000");
        await FinishedHandoffAsync(server);

        using (var status = await new ApiTestClient(server).GetAsync("/api/v1/intake/handoffs/deluno/h1", SecretHeader))
        {
            Assert.Equal("completed", (await Json(status))["state"]!.GetValue<string>());
        }

        using var response = await PostOutcomeAsync(server, "h1", DelunoOutcome("imported", "/media/movies/Film/film.mkv", null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_failed_row_left_for_the_release_folder_by_an_earlier_hand_off_does_not_fail_this_one()
    {
        // A failed row for the release folder itself, left days earlier, sits under the new hand-off's path and must
        // not be counted with the file Weir has just finished inside it.
        await using var server = await StartAsync();
        var library = await MoviesAsync(server);
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO files (library_id, relative_path, status, status_reason, updated_at) " +
            "VALUES ($l, 'Film', 'processing_failed', 'Weir could not find this file under the saved watched folder.', '2026-09-19 14:48:21.000000')",
            ("$l", library));
        Directory.CreateDirectory(Path.Join(Watched, "Film"));
        await File.WriteAllTextAsync(Path.Join(Watched, "Film", "film.mkv"), "the original download");
        var handoff = new { eventType = "deluno.processor-handoff", handoffId = "h2", libraryId = "lib-1", mediaType = "movies", sourcePath = Path.Join(Watched, "Film"), callbackPath = "/api/integrations/processors/events" };
        using (var queued = await new ApiTestClient(server).PostAsync("/api/v1/intake/webhook/deluno", handoff, SecretHeader))
        {
            Assert.Equal(HttpStatusCode.OK, queued.StatusCode);
        }

        await TestDatabase.ExecuteAsync(server, "UPDATE jobs SET status = 'completed' WHERE job_kind = 'processing.file.remux_pass.v1'");
        await HandedBackAsync(server, library, "Film/film.mkv");

        using (var status = await new ApiTestClient(server).GetAsync("/api/v1/intake/handoffs/deluno/h2", SecretHeader))
        {
            Assert.Equal("completed", (await Json(status))["state"]!.GetValue<string>());
        }

        using var response = await PostOutcomeAsync(server, "h2", DelunoOutcome("imported", "/media/movies/Film/film.mkv", null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_file_that_fails_during_this_hand_off_still_fails_it_though_its_row_was_written_at_receipt()
    {
        // Receiving the hand-off writes the file's row a moment before the hand-off's own; a failure that leaves that time
        // alone must still count. The contract suite caught the first version of the fix setting this aside.
        await using var server = await StartAsync();
        var library = await MoviesAsync(server);
        Directory.CreateDirectory(Path.Join(Watched, "Film"));
        await File.WriteAllTextAsync(Path.Join(Watched, "Film", "film.mkv"), "the original download");
        var handoff = new { eventType = "deluno.processor-handoff", handoffId = "h3", libraryId = "lib-1", mediaType = "movies", sourcePath = Path.Join(Watched, "Film", "film.mkv"), callbackPath = "/api/integrations/processors/events" };
        using (var queued = await new ApiTestClient(server).PostAsync("/api/v1/intake/webhook/deluno", handoff, SecretHeader))
        {
            Assert.Equal(HttpStatusCode.OK, queued.StatusCode);
        }

        await TestDatabase.ExecuteAsync(server, "UPDATE jobs SET status = 'completed' WHERE job_kind = 'processing.file.remux_pass.v1'");
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO files (library_id, relative_path, status) VALUES ($l, 'Film/film.mkv', 'processing_failed') " +
            "ON CONFLICT (library_id, relative_path) DO UPDATE SET status = 'processing_failed'",
            ("$l", library));

        using var status = await new ApiTestClient(server).GetAsync("/api/v1/intake/handoffs/deluno/h3", SecretHeader);
        Assert.Equal("failed", (await Json(status))["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_failed_job_queued_for_this_hand_off_still_fails_it()
    {
        await using var server = await StartAsync();
        await FinishedHandoffAsync(server);
        await FailedPassThroughAsync(server, "2099-01-01 00:00:00.000000");

        using var status = await new ApiTestClient(server).GetAsync("/api/v1/intake/handoffs/deluno/h1", SecretHeader);
        Assert.Equal("failed", (await Json(status))["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_outcome_endpoint_answers_404_409_422_and_401_as_agreed()
    {
        await using var server = await StartAsync();
        await MoviesAsync(server);

        using (var never = await PostOutcomeAsync(server, "nobody", DelunoOutcome("imported", null, null)))
        {
            Assert.Equal((HttpStatusCode.NotFound, "Weir has never received this hand-off."), (never.StatusCode, await Detail(never)));
        }

        // Received but not finished: its pass is still queued.
        var source = Path.Join(Watched, "Queued", "queued.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await File.WriteAllTextAsync(source, "a download still waiting");
        var handoff = new { eventType = "deluno.processor-handoff", handoffId = "h2", libraryId = "lib-1", mediaType = "movies", sourcePath = source, callbackPath = "/cb" };
        using (var queued = await new ApiTestClient(server).PostAsync("/api/v1/intake/webhook/deluno", handoff, SecretHeader))
        {
            Assert.Equal(HttpStatusCode.OK, queued.StatusCode);
        }

        using (var early = await PostOutcomeAsync(server, "h2", DelunoOutcome("imported", null, null)))
        {
            Assert.Equal(
                (HttpStatusCode.Conflict, "Weir has not finished this hand-off yet (it is queued), so there is no file to import."),
                (early.StatusCode, await Detail(early)));
        }

        using (var badOutcome = await PostOutcomeAsync(server, "h2", TestDatabase.RawJson("""{"outcome":"maybe","occurredUtc":"2026-09-23T10:00:00Z"}""")))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, badOutcome.StatusCode);
            Assert.Equal("literal_error", (await Json(badOutcome))["detail"]![0]!["type"]!.GetValue<string>());
        }

        using (var noTime = await PostOutcomeAsync(server, "h2", TestDatabase.RawJson("""{"outcome":"imported"}""")))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, noTime.StatusCode);
            Assert.Equal("missing", (await Json(noTime))["detail"]![0]!["type"]!.GetValue<string>());
        }

        using (var naive = await PostOutcomeAsync(server, "h2", TestDatabase.RawJson("""{"outcome":"imported","occurredUtc":"2026-09-23T10:00:00"}""")))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, naive.StatusCode);
        }

        using (var notAnObject = await PostOutcomeAsync(server, "h2", TestDatabase.RawJson("[1]")))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, notAnObject.StatusCode);
        }

        using var noSecret = await PostOutcomeAsync(server, "h2", DelunoOutcome("imported", null, null), withSecret: false);
        Assert.Equal(HttpStatusCode.Unauthorized, noSecret.StatusCode);
    }
}
