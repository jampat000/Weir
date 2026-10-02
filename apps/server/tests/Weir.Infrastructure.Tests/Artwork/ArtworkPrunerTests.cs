using Weir.Core.Artwork;
using Weir.Infrastructure.Artwork;

namespace Weir.Infrastructure.Tests.Artwork;

/// <summary>Poster data goes when the files that used it have been gone long enough, and not before.</summary>
public sealed class ArtworkPrunerTests
{
    private const string PosterFile = "zv7J85D8CC9qYagAEhPM63CIG6j.jpg";
    private const string FilmPath = "Nosferatu (1922)/Nosferatu.1922.1080p.mkv";

    private static string PosterPath(ArtworkFixture fixture, string posterFile = PosterFile) =>
        Path.Join(fixture.Store.Options.WeirHome, "artwork", "posters", ArtworkKeys.PosterId(posterFile));

    /// <summary>A file Weir knows, with its poster found and stored.</summary>
    private static async Task<long> FileWithPosterAsync(ArtworkFixture fixture, string path = FilmPath, string posterFile = PosterFile)
    {
        fixture.ServeSearch(ArtworkFixture.SearchAnswer(posterFile)).ServeImage(posterFile);
        var library = await fixture.LibraryIdAsync("movie");
        await fixture.InsertFileAsync(library, path);
        await fixture.Discovery.DiscoverAsync(CancellationToken.None);
        await fixture.Resolver.ResolveDueAsync(5, CancellationToken.None);
        fixture.Store.Clock.Advance(ArtworkRateLimiter.MinimumBetweenSearches);
        return library;
    }

    private static Task ForgetFileAsync(ArtworkFixture fixture, string path) =>
        fixture.Store.Execute($"DELETE FROM files WHERE relative_path = '{path}'");

    [Fact]
    public async Task A_file_that_is_gone_keeps_its_poster_through_the_grace_and_loses_all_of_it_after()
    {
        using var fixture = new ArtworkFixture();
        await FileWithPosterAsync(fixture);
        await ForgetFileAsync(fixture, FilmPath);

        await fixture.Pruner.PruneAsync(CancellationToken.None);
        fixture.Store.Clock.Advance(TimeSpan.FromDays(29));
        var early = await fixture.Pruner.PruneAsync(CancellationToken.None);

        Assert.Equal(0, early);
        Assert.Equal(1, await fixture.Store.Scalar("SELECT count(*) FROM artwork_posters"));
        Assert.True(File.Exists(PosterPath(fixture)));

        fixture.Store.Clock.Advance(TimeSpan.FromDays(2));
        var removed = await fixture.Pruner.PruneAsync(CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.Equal(0, await fixture.Store.Scalar("SELECT count(*) FROM artwork_files"));
        Assert.Equal(0, await fixture.Store.Scalar("SELECT count(*) FROM artwork_lookups"));
        Assert.Equal(0, await fixture.Store.Scalar("SELECT count(*) FROM artwork_posters"));
        Assert.False(File.Exists(PosterPath(fixture)));
    }

    [Fact]
    public async Task A_file_that_comes_back_within_the_grace_keeps_everything()
    {
        using var fixture = new ArtworkFixture();
        var library = await FileWithPosterAsync(fixture);
        await ForgetFileAsync(fixture, FilmPath);
        await fixture.Pruner.PruneAsync(CancellationToken.None);
        fixture.Store.Clock.Advance(TimeSpan.FromDays(20));
        await fixture.InsertFileAsync(library, FilmPath);
        await fixture.Pruner.PruneAsync(CancellationToken.None);

        fixture.Store.Clock.Advance(TimeSpan.FromDays(60));
        await fixture.Pruner.PruneAsync(CancellationToken.None);

        Assert.Equal(1, await fixture.Store.Scalar("SELECT count(*) FROM artwork_files WHERE orphaned_at IS NULL"));
        Assert.True(File.Exists(PosterPath(fixture)));
        Assert.Single(await fixture.PosterUrlsForAsync(library, FilmPath));
    }

    [Fact]
    public async Task A_poster_another_title_still_uses_stays_when_one_title_goes()
    {
        using var fixture = new ArtworkFixture();
        var library = await FileWithPosterAsync(fixture);
        await FileWithPosterAsync(fixture, "Nosferatu.1979.mkv");
        await ForgetFileAsync(fixture, FilmPath);
        await fixture.Pruner.PruneAsync(CancellationToken.None);

        fixture.Store.Clock.Advance(TimeSpan.FromDays(31));
        var removed = await fixture.Pruner.PruneAsync(CancellationToken.None);

        Assert.Equal(0, removed);
        Assert.Equal(1, await fixture.Store.Scalar("SELECT count(*) FROM artwork_lookups"));
        Assert.Equal(1, await fixture.Store.Scalar("SELECT count(*) FROM artwork_posters"));
        Assert.True(File.Exists(PosterPath(fixture)));
        Assert.Single(await fixture.PosterUrlsForAsync(library, "Nosferatu.1979.mkv"));
    }

    [Fact]
    public async Task A_title_the_service_did_not_know_is_forgotten_with_its_files()
    {
        using var fixture = new ArtworkFixture();
        fixture.Http.Json(HttpMethod.Get, "/metadata/search", """{"results":[]}""");
        await fixture.InsertFileAsync(await fixture.LibraryIdAsync("movie"), FilmPath);
        await fixture.Discovery.DiscoverAsync(CancellationToken.None);
        await fixture.Resolver.ResolveDueAsync(5, CancellationToken.None);
        await ForgetFileAsync(fixture, FilmPath);
        await fixture.Pruner.PruneAsync(CancellationToken.None);

        fixture.Store.Clock.Advance(TimeSpan.FromDays(31));
        await fixture.Pruner.PruneAsync(CancellationToken.None);

        Assert.Equal(0, await fixture.Store.Scalar("SELECT count(*) FROM artwork_lookups"));
    }

    [Fact]
    public async Task A_file_in_a_library_scan_counts_as_a_file_weir_still_knows()
    {
        using var fixture = new ArtworkFixture();
        var library = await FileWithPosterAsync(fixture);
        await fixture.Store.Execute(
            $"INSERT INTO library_files (library_id, path, size_bytes, mtime, classification) VALUES ({library}, '{FilmPath}', 1, 1, 'matches')");
        await ForgetFileAsync(fixture, FilmPath);

        await fixture.Pruner.PruneAsync(CancellationToken.None);
        fixture.Store.Clock.Advance(TimeSpan.FromDays(31));
        await fixture.Pruner.PruneAsync(CancellationToken.None);

        Assert.Equal(1, await fixture.Store.Scalar("SELECT count(*) FROM artwork_files"));
        Assert.True(File.Exists(PosterPath(fixture)));
    }

    [Fact]
    public async Task A_poster_image_already_missing_from_disk_does_not_stop_the_pass()
    {
        using var fixture = new ArtworkFixture();
        await FileWithPosterAsync(fixture);
        File.Delete(PosterPath(fixture));
        await ForgetFileAsync(fixture, FilmPath);
        await fixture.Pruner.PruneAsync(CancellationToken.None);

        fixture.Store.Clock.Advance(TimeSpan.FromDays(31));

        Assert.Equal(1, await fixture.Pruner.PruneAsync(CancellationToken.None));
        Assert.Equal(0, await fixture.Store.Scalar("SELECT count(*) FROM artwork_posters"));
    }

    [Fact]
    public async Task The_background_pass_prunes_even_with_no_gateway_and_no_more_than_once_an_hour()
    {
        using var fixture = new ArtworkFixture();
        await FileWithPosterAsync(fixture);
        var offline = fixture.SwitchedOffGateway();
        var task = fixture.TaskFor(offline);
        await task.RunOnceAsync(CancellationToken.None);
        await ForgetFileAsync(fixture, FilmPath);

        fixture.Store.Clock.Advance(TimeSpan.FromMinutes(30));
        await task.RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, await fixture.Store.Scalar("SELECT count(*) FROM artwork_files WHERE orphaned_at IS NOT NULL"));

        fixture.Store.Clock.Advance(TimeSpan.FromMinutes(31));
        await task.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, await fixture.Store.Scalar("SELECT count(*) FROM artwork_files WHERE orphaned_at IS NOT NULL"));

        fixture.Store.Clock.Advance(TimeSpan.FromDays(31));
        await task.RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, await fixture.Store.Scalar("SELECT count(*) FROM artwork_posters"));
    }
}
