using Weir.Core.Artwork;
using Weir.Core.Json;
using Weir.Core.MediaManagers;

namespace Weir.Core.Tests.Artwork;

/// <summary>What Deluno's hand-off says about a title, read leniently so artwork never fails a hand-off.</summary>
public sealed class ArtworkHintsTests
{
    private const string OriginalEight =
        """ "eventType":"deluno.processor-handoff","handoffId":"h1","libraryId":"lib-1","mediaType":"movies","sourcePath":"/in/film.mkv","releaseName":"Film.1999.1080p","queueItemId":"q1","callbackPath":"/events" """;

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
}
