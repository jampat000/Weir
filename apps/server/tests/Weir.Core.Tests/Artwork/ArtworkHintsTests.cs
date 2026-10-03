using Weir.Core.Artwork;
using Weir.Core.Json;
using Weir.Core.MediaManagers;

namespace Weir.Core.Tests.Artwork;

/// <summary>What Deluno's hand-off says about a title, read leniently so artwork never fails a hand-off.</summary>
public sealed class ArtworkHintsTests
{
    private const string OriginalEight =
        """ "eventType":"deluno.processor-handoff","handoffId":"h1","libraryId":"lib-1","mediaType":"movies","sourcePath":"/in/film.mkv","releaseName":"Film.1999.1080p","queueItemId":"q1","callbackPath":"/events" """;

    private static WireObject Json(string json) => (WireObject)WireJsonParser.Parse(json);

    private static WireObject Payload(string extraFields) =>
        (WireObject)WireJsonParser.Parse("{" + OriginalEight + (extraFields.Length > 0 ? "," + extraFields : string.Empty) + "}");

    [Fact]
    public void Every_field_Deluno_sends_is_read()
    {
        var hints = ArtworkHints.FromHandoff(Payload(
            """ "title":"Film","year":1999,"tmdbId":603,"tvdbId":78804,"imdbId":"tt0133093","season":2,"episode":5,"posterUrl":"https://image.tmdb.org/t/p/w500/abcd1234.jpg" """));

        Assert.Equal(new ArtworkHints("Film", 1999, 603, 78804, "tt0133093", 2, 5, "abcd1234.jpg"), hints);
    }

    [Fact]
    public void A_hand_off_with_only_its_original_keys_has_no_hints() =>
        Assert.Null(ArtworkHints.FromHandoff(Payload(string.Empty)));

    [Fact]
    public void A_malformed_field_is_left_out_and_the_others_are_kept()
    {
        var hints = ArtworkHints.FromHandoff(Payload(
            """ "title":"Film","year":"nineteen","tmdbId":-4,"tvdbId":1.5,"imdbId":"not-an-id","season":"two","posterUrl":"https://evil.example/p.jpg" """));

        Assert.Equal(new ArtworkHints("Film", null, null, null, null, null, null, null), hints);
    }

    [Fact]
    public void A_poster_address_off_the_allowed_hosts_is_ignored_so_the_ids_decide()
    {
        var hints = ArtworkHints.FromHandoff(Payload(""" "tmdbId":603,"posterUrl":"http://image.tmdb.org/t/p/w500/abcd1234.jpg" """));

        Assert.Equal(new ArtworkHints(null, null, 603, null, null, null, null, null), hints);
    }

    [Fact]
    public void A_title_longer_than_the_service_accepts_is_left_out()
    {
        var hints = ArtworkHints.FromHandoff(Payload($"\"tmdbId\":603,\"title\":\"{new string('a', ArtworkTitleReader.MaxTitleLength + 1)}\""));

        Assert.Null(hints?.Title);
    }

    [Fact]
    public void The_deluno_dialect_carries_the_hints_beside_the_original_fields()
    {
        var imported = ImportEvents.DialectForSource("deluno")!.Normalize(Payload(""" "title":"Film","tmdbId":603 """));

        Assert.Equal("Film.1999.1080p", imported!.ReleaseName);
        Assert.Equal(603L, imported.Artwork!.TmdbId);
    }

    [Fact]
    public void The_deluno_dialect_accepts_a_hand_off_with_no_hints()
    {
        var imported = ImportEvents.DialectForSource("deluno")!.Normalize(Payload(string.Empty));

        Assert.Null(imported!.Artwork);
        Assert.Equal("/in/film.mkv", imported.FilePath);
    }

    [Fact]
    public void A_radarr_import_carries_the_movies_tmdb_and_imdb_ids()
    {
        var imported = ImportEvents.DialectForSource("radarr")!.Normalize(Json("""
            {"eventType":"Download","movie":{"id":7,"title":"The Long Tide","year":2024,"tmdbId":8841,"imdbId":"tt1234567"},
             "movieFile":{"path":"/movies/x.mkv"}}
            """));

        Assert.Equal(new ArtworkHints("The Long Tide", 2024, 8841, null, "tt1234567", null, null, null), imported!.Artwork);
    }

    [Fact]
    public void A_sonarr_import_carries_the_series_tvdb_and_imdb_ids_and_the_episode()
    {
        var imported = ImportEvents.DialectForSource("sonarr")!.Normalize(Json("""
            {"eventType":"Download","series":{"id":3,"title":"Paper Lanterns","tvdbId":121361,"imdbId":"tt7654321"},
             "episodes":[{"id":41,"seasonNumber":1,"episodeNumber":2}],"episodeFile":{"path":"/tv/x.mkv"}}
            """));

        Assert.Equal(new ArtworkHints("Paper Lanterns", null, null, 121361, "tt7654321", 1, 2, null), imported!.Artwork);
    }

    [Fact]
    public void An_import_whose_ids_are_missing_or_malformed_carries_no_hints()
    {
        var radarr = ImportEvents.DialectForSource("radarr")!.Normalize(Json("""
            {"eventType":"Download","movie":{"id":7,"tmdbId":"abc","imdbId":5},"movieFile":{"path":"/movies/x.mkv"}}
            """));
        var sonarr = ImportEvents.DialectForSource("sonarr")!.Normalize(Json("""
            {"eventType":"Download","episodes":[{"id":41}],"episodeFile":{"path":"/tv/x.mkv"}}
            """));

        Assert.Null(radarr!.Artwork);
        Assert.Null(sonarr!.Artwork);
    }
}
