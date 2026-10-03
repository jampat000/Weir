using System.Net;
using Weir.Api.Tests.Platform;
using Weir.Core.Artwork;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.MediaManagers;

/// <summary>
/// Radarr's and Sonarr's import messages carry exact ids for the title. When the file is one Weir handed back, those ids
/// give it its poster instead of a guess at its name. Simulated media only.
/// </summary>
public sealed class ImportArtworkApiTests : IDisposable
{
    private const string WebhookSecret = "s3cret";
    private const string MovieCopy = "The.Long.Tide.2024/The.Long.Tide.2024.mkv";
    private const string EpisodeCopy = "Paper.Lanterns.S01E02/Paper.Lanterns.S01E02.mkv";

    private static readonly Dictionary<string, string> SecretHeader = new() { ["X-Webhook-Secret"] = WebhookSecret };

    private readonly string _root = Path.Join(Path.GetTempPath(), "weir-import-artwork-" + Guid.NewGuid().ToString("N"));

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

    /// <summary>A file of a library whose cleaned copy Weir wrote into the output folder, recorded as a pass records it.</summary>
    private async Task<long> HandedBackAsync(WeirTestServer server, string mediaType, string relative)
    {
        await TestDatabase.ExecuteAsync(server, "UPDATE libraries SET watched_folder = $w, output_folder = $o WHERE media_type = $t", ("$w", Path.Join(_root, "downloads")), ("$o", Output), ("$t", mediaType));
        var library = await TestDatabase.ScalarAsync(server, "SELECT id FROM libraries WHERE media_type = $t ORDER BY id LIMIT 1", ("$t", mediaType));
        var copy = Path.Join(Output, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        await File.WriteAllTextAsync(copy, "the cleaned copy");
        var info = new FileInfo(copy);
        await TestDatabase.ExecuteAsync(server, "INSERT INTO files (library_id, relative_path, status) VALUES ($l, $p, 'processed')", ("$l", library), ("$p", relative));
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO handbacks (library_id, relative_path, output_path, output_size, output_mtime_ns, written_at) VALUES ($l, $p, $o, $s, $m, '2026-09-20 10:00:00.000000')",
            ("$l", library),
            ("$p", relative),
            ("$o", copy),
            ("$s", info.Length),
            ("$m", (info.LastWriteTimeUtc - DateTime.UnixEpoch).Ticks * 100));
        return library;
    }

    private static object RadarrImport(object movie) => new
    {
        eventType = "Download",
        movie,
        movieFile = new { path = "/movies/The Long Tide (2024)/The Long Tide (2024).mkv", sourcePath = "/mnt/weir-out/" + MovieCopy },
        downloadId = "A1B2C3",
    };

    [Fact]
    public async Task A_radarr_import_gives_the_file_its_exact_title_for_its_poster()
    {
        await using var server = await StartAsync();
        await HandedBackAsync(server, "movie", MovieCopy);
        var movie = new { id = 7, title = "The Long Tide", year = 2024, tmdbId = 8841, imdbId = "tt1234567" };

        using var response = await new ApiTestClient(server).PostAsync("/api/v1/intake/webhook/radarr", RadarrImport(movie), SecretHeader);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var key = ArtworkKeys.ForTmdbId("movie", 8841);
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM artwork_files WHERE lookup_key = $k AND relative_path = $p", ("$k", key), ("$p", MovieCopy)));
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM artwork_lookups WHERE lookup_key = $k AND imdb_id = 'tt1234567' AND year = 2024", ("$k", key)));
    }

    [Fact]
    public async Task A_sonarr_import_names_the_series_by_its_tvdb_id()
    {
        await using var server = await StartAsync();
        await HandedBackAsync(server, "tv", EpisodeCopy);
        var sonarr = new
        {
            eventType = "Download",
            series = new { id = 3, title = "Paper Lanterns", tvdbId = 121361, imdbId = "tt7654321" },
            episodes = new[] { new { id = 41, seasonNumber = 1, episodeNumber = 2, title = "The Second" } },
            episodeFile = new { path = "/tv/Paper Lanterns/Season 01/Paper Lanterns - S01E02.mkv", sourcePath = "/mnt/weir-out/" + EpisodeCopy },
            downloadId = "A1B2C3",
        };

        using var response = await new ApiTestClient(server).PostAsync("/api/v1/intake/webhook/sonarr", sonarr, SecretHeader);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM artwork_lookups WHERE media_scope = 'tv' AND tvdb_id = 121361 AND imdb_id = 'tt7654321' AND title = 'Paper Lanterns'"));
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM artwork_files WHERE season = 1 AND episode = 2 AND lookup_key IS NOT NULL"));
    }

    [Fact]
    public async Task An_import_with_malformed_ids_is_still_recorded_and_the_ids_are_ignored()
    {
        await using var server = await StartAsync();
        await HandedBackAsync(server, "movie", MovieCopy);

        using var response = await new ApiTestClient(server).PostAsync(
            "/api/v1/intake/webhook/radarr", RadarrImport(new { id = 7, title = "The Long Tide", tmdbId = "abc", imdbId = 5 }), SecretHeader);

        Assert.Contains("\"matched\":true", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM artwork_lookups WHERE lookup_key LIKE 'tmdb:%'"));
    }

    [Fact]
    public async Task An_import_with_no_ids_leaves_the_files_title_as_it_was()
    {
        await using var server = await StartAsync();
        await HandedBackAsync(server, "movie", MovieCopy);

        using var response = await new ApiTestClient(server).PostAsync("/api/v1/intake/webhook/radarr", RadarrImport(new { id = 7 }), SecretHeader);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM artwork_lookups"));
    }

    [Fact]
    public async Task An_import_with_no_webhook_secret_could_be_from_anybody_so_it_changes_no_poster_title()
    {
        await using var server = await StartAsync(webhookSecret: string.Empty);
        await HandedBackAsync(server, "movie", MovieCopy);
        await TestDatabase.ExecuteAsync(server, "INSERT INTO media_manager_connections (kind, name, base_url) VALUES ('radarr', 'Radarr', 'http://192.0.2.20:7878')");

        using var response = await new ApiTestClient(server).PostAsync("/api/v1/intake/webhook/radarr", RadarrImport(new { id = 7, title = "The Long Tide", tmdbId = 8841 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"matched\":true", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM artwork_lookups"));
    }
}
