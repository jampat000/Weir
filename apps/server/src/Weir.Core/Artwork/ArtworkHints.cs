using System.Text.RegularExpressions;
using Weir.Core.Json;
using Weir.Core.MediaManagers;

namespace Weir.Core.Artwork;

/// <summary>
/// What a media manager says about a title in its hand-off (Deluno's optional <c>title</c>, <c>year</c>, <c>tmdbId</c>,
/// <c>tvdbId</c>, <c>imdbId</c>, <c>season</c>, <c>episode</c> and <c>posterUrl</c>). Every field is optional and a malformed
/// one is left out, so artwork never makes a hand-off fail.
/// </summary>
public sealed partial record ArtworkHints(
    string? Title,
    int? Year,
    long? TmdbId,
    long? TvdbId,
    string? ImdbId,
    int? Season,
    int? Episode,
    string? PosterRef)
{
    private const int FirstYear = 1888;
    private const int LastYear = 2100;

    [GeneratedRegex(@"^tt\d{5,10}$")]
    private static partial Regex ImdbIdPattern();

    /// <summary>
    /// The hints in a media manager's own record of a title (Radarr's <c>movie</c>, Sonarr's <c>series</c>): its name, year and ids.
    /// Null when the record carries none of them.
    /// </summary>
    public static ArtworkHints? FromTitleRecord(WireObject record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var hints = new ArtworkHints(
            Title: ReadTitle(record.Get("title")),
            Year: (int?)ReadNumber(record.Get("year"), FirstYear, LastYear),
            TmdbId: ReadNumber(record.Get("tmdbId"), 1, long.MaxValue),
            TvdbId: ReadNumber(record.Get("tvdbId"), 1, long.MaxValue),
            ImdbId: ReadImdbId(record.Get("imdbId")),
            Season: null,
            Episode: null,
            PosterRef: null);
        return hints == Empty ? null : hints;
    }

    /// <summary>The hints a hand-off body carries, or null when it carries none.</summary>
    public static ArtworkHints? FromHandoff(WireObject body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var hints = new ArtworkHints(
            Title: ReadTitle(body.Get("title")),
            Year: (int?)ReadNumber(body.Get("year"), FirstYear, LastYear),
            TmdbId: ReadNumber(body.Get("tmdbId"), 1, long.MaxValue),
            TvdbId: ReadNumber(body.Get("tvdbId"), 1, long.MaxValue),
            ImdbId: ReadImdbId(body.Get("imdbId")),
            Season: (int?)ReadNumber(body.Get("season"), 0, int.MaxValue),
            Episode: (int?)ReadNumber(body.Get("episode"), 0, int.MaxValue),
            PosterRef: ArtworkPosterSource.RefFromHandoffUrl(ManagerValues.Text(body.Get("posterUrl"))));
        return hints == Empty ? null : hints;
    }

    private static ArtworkHints Empty { get; } = new(null, null, null, null, null, null, null, null);

    private static string? ReadImdbId(WireValue? value) =>
        ManagerValues.Text(value) is { } imdb && ImdbIdPattern().IsMatch(imdb) ? imdb : null;

    private static string? ReadTitle(WireValue? value) =>
        ManagerValues.Text(value) is { Length: <= ArtworkTitleReader.MaxTitleLength } title ? title : null;

    private static long? ReadNumber(WireValue? value, long minimum, long maximum) =>
        ManagerValues.WholeNumber(value) is { } number && number >= minimum && number <= maximum
            ? (long)number
            : null;
}
