using Weir.Infrastructure.Artwork;

namespace Weir.Infrastructure.Tests.Artwork;

/// <summary>Titling files that arrive without a hand-off, and the background pass that does it.</summary>
public sealed class ArtworkDiscoveryTests
{
    private const string PosterFile = "zv7J85D8CC9qYagAEhPM63CIG6j.jpg";

    [Fact]
    public async Task A_file_weir_has_seen_is_titled_from_its_name_and_queued()
    {
        using var fixture = new ArtworkFixture();
        var library = await fixture.LibraryIdAsync("movie");
        await fixture.InsertFileAsync(library, "Charade (1963)/Charade.1963.1080p.mkv");

        await fixture.Discovery.DiscoverAsync(CancellationToken.None);

        Assert.Equal(1, await fixture.Store.Scalar($"SELECT count(*) FROM artwork_files WHERE library_id = {library} AND lookup_key IS NOT NULL"));
        Assert.Equal(1, await fixture.Store.Scalar("SELECT count(*) FROM artwork_lookups WHERE outcome = 'pending' AND title = 'charade' AND year = 1963"));
    }

    [Fact]
    public async Task A_file_is_titled_once_however_often_the_pass_runs()
    {
        using var fixture = new ArtworkFixture();
        await fixture.InsertFileAsync(await fixture.LibraryIdAsync("movie"), "Charade.1963.mkv");

        await fixture.Discovery.DiscoverAsync(CancellationToken.None);
        await fixture.Discovery.DiscoverAsync(CancellationToken.None);

        Assert.Equal(1, await fixture.Store.Scalar("SELECT count(*) FROM artwork_files"));
        Assert.Equal(1, await fixture.Store.Scalar("SELECT count(*) FROM artwork_lookups"));
    }

    [Fact]
    public async Task A_file_whose_name_gives_no_title_is_recorded_so_it_is_not_read_again()
    {
        using var fixture = new ArtworkFixture();
        await fixture.InsertFileAsync(await fixture.LibraryIdAsync("movie"), "1080p.x265.mkv");

        await fixture.Discovery.DiscoverAsync(CancellationToken.None);

        Assert.Equal(1, await fixture.Store.Scalar("SELECT count(*) FROM artwork_files WHERE lookup_key IS NULL"));
        Assert.Equal(0, await fixture.Store.Scalar("SELECT count(*) FROM artwork_lookups"));
    }

    [Fact]
    public async Task Every_episode_of_a_series_is_titled_with_the_series()
    {
        using var fixture = new ArtworkFixture();
        var library = await fixture.LibraryIdAsync("tv");
        foreach (var episode in new[] { "Example Show/Season 1/Example.Show.S01E01.mkv", "Example Show/Season 1/Example.Show.S01E02.mkv" })
        {
            await fixture.InsertFileAsync(library, episode);
        }

        await fixture.Discovery.DiscoverAsync(CancellationToken.None);

        Assert.Equal(1, await fixture.Store.Scalar("SELECT count(*) FROM artwork_lookups WHERE media_scope = 'tv'"));
        Assert.Equal(2, await fixture.Store.Scalar("SELECT count(*) FROM artwork_files"));
    }

    [Fact]
    public async Task A_library_scan_file_is_titled_by_the_name_its_manager_gave_it_and_waits_behind_downloads()
    {
        using var fixture = new ArtworkFixture();
        var library = await fixture.LibraryIdAsync("movie");
        await fixture.Store.Execute(
            "INSERT INTO library_files (library_id, path, size_bytes, mtime, classification, manager_title) " +
            $"VALUES ({library}, 'D:/Movies/abc123/video.mkv', 1, 1, 'matches', 'Metropolis')");

        await fixture.Discovery.DiscoverAsync(CancellationToken.None);

        Assert.Equal(1, await fixture.Store.Scalar("SELECT count(*) FROM artwork_lookups WHERE title = 'metropolis' AND priority = 0"));
    }

    [Fact]
    public async Task The_background_pass_titles_new_files_and_looks_their_posters_up()
    {
        using var fixture = new ArtworkFixture();
        fixture.ServeSearch(ArtworkFixture.SearchAnswer(PosterFile)).ServeImage(PosterFile);
        var library = await fixture.LibraryIdAsync("movie");
        await fixture.InsertFileAsync(library, "Charade.1963.mkv");
        var task = fixture.TaskFor(fixture.Gateway);

        await task.RunOnceAsync(CancellationToken.None);

        Assert.Single(await fixture.PosterUrlsForAsync(library, "Charade.1963.mkv"));
    }

    [Fact]
    public async Task The_background_pass_does_nothing_while_artwork_is_off()
    {
        using var fixture = new ArtworkFixture();
        await fixture.Store.Execute("UPDATE suite_settings SET artwork_enabled = 0 WHERE id = 1");
        await fixture.InsertFileAsync(await fixture.LibraryIdAsync("movie"), "Charade.1963.mkv");
        var task = fixture.TaskFor(fixture.Gateway);

        await task.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, await fixture.Store.Scalar("SELECT count(*) FROM artwork_files"));
        Assert.Empty(fixture.Http.Requests);
    }

    [Fact]
    public async Task The_background_pass_does_nothing_when_no_gateway_is_configured()
    {
        using var fixture = new ArtworkFixture();
        var unconfigured = new ArtworkGatewayClient(
            Core.Configuration.WeirOptionsLoader.Load(new Core.Configuration.RuntimeEnvironment(
                new Dictionary<string, string> { ["WEIR_HOME"] = fixture.Store.Home.Path, ["WEIR_ARTWORK_GATEWAY_URL"] = "off" },
                OperatingSystem.IsWindows(),
                fixture.Store.Home.Path,
                fixture.Store.Home.Path)),
            fixture.Http,
            fixture.Store.Clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ArtworkGatewayClient>.Instance);
        await fixture.InsertFileAsync(await fixture.LibraryIdAsync("movie"), "Charade.1963.mkv");
        var task = fixture.TaskFor(unconfigured);

        await task.RunOnceAsync(CancellationToken.None);

        Assert.False(unconfigured.IsConfigured);
        Assert.Equal(0, await fixture.Store.Scalar("SELECT count(*) FROM artwork_files"));
    }
}
