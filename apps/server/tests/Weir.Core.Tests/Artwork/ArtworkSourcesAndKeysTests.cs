using Weir.Core.Artwork;

namespace Weir.Core.Tests.Artwork;

/// <summary>Which poster addresses Weir will fetch from, and the keys lookups and posters are stored under.</summary>
public sealed class ArtworkSourcesAndKeysTests
{
    [Theory]
    [InlineData("https://image.tmdb.org/t/p/w500/zv7J85D8CC9qYagAEhPM63CIG6j.jpg", "zv7J85D8CC9qYagAEhPM63CIG6j.jpg")]
    [InlineData("https://deluno-metadata-gateway.ejmdigital.workers.dev/artwork/w780/zv7J85D8CC9qYagAEhPM63CIG6j.jpg", "zv7J85D8CC9qYagAEhPM63CIG6j.jpg")]
    [InlineData("https://artworks.thetvdb.com/banners/posters/78804-52.jpg", "https://artworks.thetvdb.com/banners/posters/78804-52.jpg")]
    [InlineData("https://m.media-amazon.com/images/M/MV5BNTk4@._V1_SX300.jpg", "https://m.media-amazon.com/images/M/MV5BNTk4@._V1_SX300.jpg")]
    public void A_hand_off_poster_on_one_of_the_four_image_hosts_is_accepted(string url, string expected) =>
        Assert.Equal(expected, ArtworkPosterSource.RefFromHandoffUrl(url));

    [Theory]
    [InlineData("http://image.tmdb.org/t/p/w500/zv7J85D8CC9qYagAEhPM63CIG6j.jpg")]
    [InlineData("https://evil.example/t/p/w500/zv7J85D8CC9qYagAEhPM63CIG6j.jpg")]
    [InlineData("https://image.tmdb.org.evil.example/zv7J85D8CC9qYagAEhPM63CIG6j.jpg")]
    [InlineData("https://image.tmdb.org/t/p/w500/not-an-image.html")]
    [InlineData("https://artworks.thetvdb.com/banners/posters/78804-52.jpg?token=secret")]
    [InlineData("https://m.media-amazon.com/images/M/..%2f..%2fetc.jpg")]
    [InlineData("/artwork/w342/zv7J85D8CC9qYagAEhPM63CIG6j.jpg")]
    [InlineData("")]
    public void A_hand_off_poster_anywhere_else_is_ignored(string url) =>
        Assert.Null(ArtworkPosterSource.RefFromHandoffUrl(url));

    [Fact]
    public void A_configured_gateway_may_answer_with_its_own_address_on_plain_http() =>
        Assert.Equal(
            "zv7J85D8CC9qYagAEhPM63CIG6j.jpg",
            ArtworkPosterSource.RefFromAnswerUrl("http://localhost:5099/artwork/w780/zv7J85D8CC9qYagAEhPM63CIG6j.jpg", "localhost"));

    [Fact]
    public void An_answer_naming_another_host_is_ignored() =>
        Assert.Null(ArtworkPosterSource.RefFromAnswerUrl("http://elsewhere.example/artwork/w780/zv7J85D8CC9qYagAEhPM63CIG6j.jpg", "localhost"));

    [Fact]
    public void A_tmdb_image_is_fetched_at_the_size_weir_shows_through_the_gateway() =>
        Assert.Equal(
            "http://localhost:5099/artwork/w342/abc123.jpg",
            ArtworkPosterSource.ImageUrl("http://localhost:5099", "abc123.jpg"));

    [Fact]
    public void An_image_from_another_host_is_fetched_from_exactly_that_address() =>
        Assert.Equal(
            "https://artworks.thetvdb.com/banners/posters/78804-52.jpg",
            ArtworkPosterSource.ImageUrl("http://localhost:5099", "https://artworks.thetvdb.com/banners/posters/78804-52.jpg"));

    [Fact]
    public void The_same_title_in_any_letter_case_and_punctuation_shares_one_lookup() =>
        Assert.Equal(
            ArtworkKeys.ForTitle("movie", new ArtworkTitle("The Terror!", 1963)),
            ArtworkKeys.ForTitle("movie", new ArtworkTitle("the terror", 1963)));

    [Fact]
    public void A_film_and_a_series_with_the_same_title_do_not_share_a_lookup() =>
        Assert.NotEqual(
            ArtworkKeys.ForTitle("movie", new ArtworkTitle("fargo", null)),
            ArtworkKeys.ForTitle("tv", new ArtworkTitle("fargo", null)));

    [Fact]
    public void A_poster_id_is_stable_and_has_the_shape_the_route_accepts()
    {
        var id = ArtworkKeys.PosterId("abc123.jpg");

        Assert.Equal(id, ArtworkKeys.PosterId("abc123.jpg"));
        Assert.NotEqual(id, ArtworkKeys.PosterId("abc124.jpg"));
        Assert.True(ArtworkKeys.IsPosterId(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../../etc/passwd")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789")]
    [InlineData("0123456789abcdef")]
    public void An_id_that_is_not_a_poster_id_is_refused(string value) =>
        Assert.False(ArtworkKeys.IsPosterId(value));

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(20, 360)]
    public void A_title_that_could_not_be_asked_waits_longer_each_time_up_to_six_hours(int failures, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), ArtworkSchedule.FailureBackoff(failures));
}
