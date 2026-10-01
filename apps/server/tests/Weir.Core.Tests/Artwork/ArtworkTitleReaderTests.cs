using Weir.Core.Artwork;

namespace Weir.Core.Tests.Artwork;

/// <summary>Which title a poster lookup asks about, read from a file's path.</summary>
public sealed class ArtworkTitleReaderTests
{
    [Theory]
    [InlineData("The.Terror.1963.1080p.WEB-DL.DDP5.1.H.264-GRP/The.Terror.1963.1080p.mkv", "the terror", 1963)]
    [InlineData("Nosferatu (1922)/Nosferatu.mkv", "nosferatu", 1922)]
    [InlineData("Nosferatu.1922.720p.BluRay.mkv", "nosferatu", 1922)]
    public void A_film_is_named_by_its_folder_or_file_with_the_release_year(string path, string title, int year) =>
        Assert.Equal(new ArtworkTitle(title, year), ArtworkTitleReader.Read("movie", path, releaseName: null));

    [Fact]
    public void A_film_in_a_folder_named_for_its_category_is_named_by_its_file()
    {
        var read = ArtworkTitleReader.Read("movie", "D:\\Movies\\Charade.1963.1080p.mkv", releaseName: null);

        Assert.Equal(new ArtworkTitle("charade", 1963), read);
    }

    [Fact]
    public void The_name_a_manager_gave_the_release_comes_first()
    {
        var read = ArtworkTitleReader.Read("movie", "incoming/abc123/video.mkv", releaseName: "Metropolis.1927.1080p.BluRay-GRP");

        Assert.Equal(new ArtworkTitle("metropolis", 1927), read);
    }

    [Theory]
    [InlineData("Example Show - S01E02 - Pilot.mkv")]
    [InlineData("Example.Show.S01E02.720p.WEB.mkv")]
    [InlineData("Example Show/Season 1/Example Show - 1x02.mkv")]
    [InlineData("Example Show/Season 01/Pilot.mkv")]
    [InlineData("Example.Show.S01.1080p.WEB-DL/episode 2.mkv")]
    public void Every_episode_of_a_series_reads_the_series_title(string path)
    {
        var read = ArtworkTitleReader.Read("tv", path, releaseName: null);

        Assert.Equal("example show", read?.Title);
    }

    [Fact]
    public void A_series_title_keeps_the_year_the_folder_names()
    {
        var read = ArtworkTitleReader.Read("tv", "Example Show (2019)/Season 2/Example Show - S02E03.mkv", releaseName: null);

        Assert.Equal(new ArtworkTitle("example show", 2019), read);
    }

    [Fact]
    public void An_absolute_series_path_reads_the_series_not_the_drive_or_season_folder()
    {
        var read = ArtworkTitleReader.Read("tv", "D:\\TV\\Example Show\\Season 3\\03.mkv", releaseName: null);

        Assert.Equal("example show", read?.Title);
    }

    [Fact]
    public void A_name_with_nothing_but_packaging_has_no_title()
    {
        Assert.Null(ArtworkTitleReader.Read("movie", "1080p.x265.mkv", releaseName: null));
    }

    [Fact]
    public void A_title_longer_than_the_service_accepts_is_cut()
    {
        var read = ArtworkTitleReader.Read("movie", string.Join(' ', Enumerable.Repeat("word", 60)) + ".mkv", releaseName: null);

        Assert.True(read!.Title.Length <= ArtworkTitleReader.MaxTitleLength);
    }
}
