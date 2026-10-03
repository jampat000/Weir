using System.Net;
using Weir.Api.Tests.Platform;
using Weir.Core.Artwork;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.MediaManagers;

/// <summary>Deluno's hand-off over HTTP, with and without the optional title fields it adds for posters.</summary>
public sealed class HandoffArtworkApiTests : IDisposable
{
    private const string WebhookSecret = "s3cret";

    private static readonly Dictionary<string, string> SecretHeader = new() { ["X-Webhook-Secret"] = WebhookSecret };

    private readonly string _root = Path.Join(Path.GetTempPath(), "weir-handoff-artwork-" + Guid.NewGuid().ToString("N"));

    public HandoffArtworkApiTests()
    {
        Directory.CreateDirectory(Watched);
    }

    private string Watched => Path.Join(_root, "downloads");

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

    private async Task<(WeirTestServer Server, string Source)> StartWithFileAsync()
    {
        var server = await WeirTestServer.StartAsync([("WEIR_SESSION_SECRET", Secret), ("WEIR_PROCESSING_WORKER_COUNT", "0"), ("WEIR_MEDIA_MANAGER_WEBHOOK_SECRET", WebhookSecret)]);
        await TestDatabase.ExecuteAsync(server, "UPDATE libraries SET watched_folder = $w WHERE media_type = 'movie'", ("$w", Watched));
        var source = Path.Join(Watched, "Film", "film.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await File.WriteAllTextAsync(source, "the original download");
        return (server, source);
    }

    private static Dictionary<string, object> Handoff(string source, params (string Name, object Value)[] extra)
    {
        var body = new Dictionary<string, object>
        {
            ["eventType"] = "deluno.processor-handoff",
            ["handoffId"] = "h1",
            ["libraryId"] = "lib-1",
            ["mediaType"] = "movies",
            ["sourcePath"] = source,
            ["releaseName"] = "Metropolis.1927.1080p.BluRay-GRP",
            ["callbackPath"] = "/api/integrations/processors/events",
        };
        foreach (var (name, value) in extra)
        {
            body[name] = value;
        }

        return body;
    }

    [Fact]
    public async Task A_hand_off_with_none_of_the_poster_fields_is_accepted_and_titled_from_its_release_name()
    {
        var (server, source) = await StartWithFileAsync();
        await using var _ = server;

        using var response = await new ApiTestClient(server).PostAsync("/api/v1/intake/webhook/deluno", Handoff(source), SecretHeader);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM artwork_lookups WHERE title = 'metropolis' AND year = 1927"));
    }

    [Fact]
    public async Task A_hand_off_with_poster_fields_is_titled_by_them()
    {
        var (server, source) = await StartWithFileAsync();
        await using var _ = server;
        var hand = Handoff(source, ("title", "Metropolis"), ("year", 1927), ("tmdbId", 19), ("imdbId", "tt0017136"), ("posterUrl", "https://image.tmdb.org/t/p/w500/abcd1234.jpg"));

        using var response = await new ApiTestClient(server).PostAsync("/api/v1/intake/webhook/deluno", hand, SecretHeader);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var key = ArtworkKeys.ForTmdbId("movie", 19);
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM artwork_lookups WHERE lookup_key = $k AND poster_ref = 'abcd1234.jpg' AND imdb_id = 'tt0017136'", ("$k", key)));
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM artwork_files WHERE lookup_key = $k AND relative_path = 'Film/film.mkv'", ("$k", key)));
    }

    [Fact]
    public async Task A_hand_off_with_malformed_poster_fields_is_still_accepted_and_the_fields_are_ignored()
    {
        var (server, source) = await StartWithFileAsync();
        await using var _ = server;
        var hand = Handoff(source, ("year", "soon"), ("tmdbId", "abc"), ("imdbId", 12), ("posterUrl", "https://evil.example/p.jpg"), ("season", -3));

        using var response = await new ApiTestClient(server).PostAsync("/api/v1/intake/webhook/deluno", hand, SecretHeader);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM artwork_lookups WHERE title = 'metropolis' AND poster_ref IS NULL"));
    }
}
