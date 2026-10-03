using Weir.Core.Artwork;
using Weir.Core.MediaManagers;
using Weir.Infrastructure.Artwork;

namespace Weir.Infrastructure.Tests.MediaManagers;

/// <summary>A hand-off titles its files for their posters: from what the manager said when it said something, from the names otherwise.</summary>
public sealed class HandoffArtworkTests
{
    private static MediaManagerImportEvent Handoff(string path, string scope, ArtworkHints? hints, string? releaseName = null) => new()
    {
        SourceKey = "deluno",
        EventKind = "handoff",
        MediaScope = scope,
        FilePath = path,
        HandoffId = "h1",
        CallbackPath = "/api/integrations/processors/events",
        LibraryId = "lib-1",
        ReleaseName = releaseName,
        Artwork = hints,
    };

    private static async Task<string> WatchedFileAsync(MediaManagerFixture fixture, string scope, string relative)
    {
        var watched = fixture.Store.Home.Join(scope);
        var file = Path.Join(watched, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "x");
        await fixture.LibraryAsync(scope, watched);
        return file;
    }

    [Fact]
    public async Task A_hand_off_with_a_tmdb_id_queues_the_title_under_that_id_and_remembers_the_season_and_episode()
    {
        using var fixture = new MediaManagerFixture();
        var file = await WatchedFileAsync(fixture, "tv", "Example Show/Example.Show.S02E05.mkv");
        var hints = new ArtworkHints("Example Show", 2019, 1399, 121361, "tt0944947", 2, 5, "abcd1234.jpg");

        await fixture.Db(uow => fixture.Intake.EnqueueRefineAsync(uow, Handoff(file, "tv", hints)));

        var key = ArtworkKeys.ForTmdbId("tv", 1399);
        Assert.Equal(1, await fixture.Store.Scalar($"SELECT count(*) FROM artwork_lookups WHERE lookup_key = '{key}' AND tvdb_id = 121361 AND imdb_id = 'tt0944947' AND poster_ref = 'abcd1234.jpg' AND year = 2019"));
        Assert.Equal(1, await fixture.Store.Scalar($"SELECT count(*) FROM artwork_files WHERE lookup_key = '{key}' AND season = 2 AND episode = 5"));
    }

    [Fact]
    public async Task A_hand_off_without_hints_is_titled_from_the_name_the_manager_gave_the_release()
    {
        using var fixture = new MediaManagerFixture();
        var file = await WatchedFileAsync(fixture, "movie", "abc123/video.mkv");

        await fixture.Db(uow => fixture.Intake.EnqueueRefineAsync(uow, Handoff(file, "movie", hints: null, releaseName: "Metropolis.1927.1080p.BluRay-GRP")));

        Assert.Equal(1, await fixture.Store.Scalar("SELECT count(*) FROM artwork_lookups WHERE title = 'metropolis' AND year = 1927 AND poster_ref IS NULL"));
        Assert.Equal(1, await fixture.Store.Scalar("SELECT count(*) FROM artwork_files WHERE lookup_key IS NOT NULL"));
    }

    [Fact]
    public async Task What_the_manager_said_replaces_a_title_already_read_from_the_name()
    {
        using var fixture = new MediaManagerFixture();
        var file = await WatchedFileAsync(fixture, "movie", "Charade.1963/Charade.1963.mkv");
        var library = await fixture.LibraryAsync("movie", Path.GetDirectoryName(Path.GetDirectoryName(file))!);
        await fixture.Db(async uow =>
        {
            await fixture.Artwork.LinkFromNameAsync(uow, new ArtworkCandidate(library, "Charade.1963/Charade.1963.mkv", "movie", null), ArtworkPriority.Processing);
            return 0;
        });

        await fixture.Db(uow => fixture.Intake.EnqueueRefineAsync(uow, Handoff(file, "movie", new ArtworkHints("Charade", 1963, 4808, null, null, null, null, null))));

        Assert.Equal(1, await fixture.Store.Scalar($"SELECT count(*) FROM artwork_files WHERE lookup_key = '{ArtworkKeys.ForTmdbId("movie", 4808)}'"));
    }
}
