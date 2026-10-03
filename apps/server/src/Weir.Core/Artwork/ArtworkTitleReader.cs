using System.Text.RegularExpressions;
using Weir.Core.Processing;
using Weir.Core.Processing.RemuxPass;

namespace Weir.Core.Artwork;

/// <summary>The title and release year a poster lookup asks about.</summary>
public sealed record ArtworkTitle(string Title, int? Year);

/// <summary>
/// Reads the title a poster lookup should ask about from a file's path, or from the name its media manager gave
/// the release. A film is named by its release name, file name or folder; a series is named before the first season or
/// episode marker, so every episode of a show reads the same title and shares one lookup.
/// </summary>
public static partial class ArtworkTitleReader
{
    private const string TvScope = "tv";

    /// <summary>The longest title the metadata service accepts.</summary>
    public const int MaxTitleLength = 160;

    [GeneratedRegex(@"^(s\d{1,2}(e\d{1,3})*|\d{1,2}x\d{2,3}|season)$")]
    private static partial Regex EpisodeMarker();

    [GeneratedRegex(@"^(season|specials|series|s\d{1,2})$")]
    private static partial Regex SeasonFolder();

    /// <summary>The title for a file of the given kind (<c>movie</c> or <c>tv</c>), or null when nothing readable remains.</summary>
    public static ArtworkTitle? Read(string mediaScope, string path, string? releaseName)
    {
        ArgumentNullException.ThrowIfNull(path);
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var clamped = mediaScope == TvScope ? ReadSeries(segments, releaseName) : ReadFilm(segments, releaseName);
        return clamped is { } found && found.Title.Length > MaxTitleLength ? found with { Title = found.Title[..MaxTitleLength].TrimEnd() } : clamped;
    }

    private static ArtworkTitle? ReadFilm(string[] segments, string? releaseName)
    {
        var fromRelease = Parse(releaseName);
        var fromFile = segments.Length > 0 ? Parse(Path.GetFileNameWithoutExtension(segments[^1])) : null;
        var fromFolder = segments.Length > 1 ? Parse(segments[^2]) : null;

        // A year tells a film's name from the folders around it ("Movies", "Downloads") that also parse as words.
        return new[] { fromRelease, fromFile, fromFolder }.FirstOrDefault(title => title?.Year is not null)
            ?? fromRelease
            ?? fromFile
            ?? fromFolder;
    }

    private static ArtworkTitle? ReadSeries(string[] segments, string? releaseName)
    {
        var candidates = new List<ArtworkTitle?> { ReadSeriesName(releaseName) };
        if (segments.Length > 0)
        {
            candidates.Add(ParseBeforeEpisodeMarker(Path.GetFileNameWithoutExtension(segments[^1])));
        }

        // Folders from the nearest outward: a season folder names no series, and a series folder is where the year usually is.
        candidates.AddRange(segments.SkipLast(1).Reverse().Where(folder => !IsSeasonFolder(folder)).Select(ReadSeriesName));
        var found = candidates.OfType<ArtworkTitle>().ToList();
        return found.FirstOrDefault(title => title.Year is not null) ?? found.FirstOrDefault();
    }

    private static ArtworkTitle? ReadSeriesName(string? name) =>
        ParseBeforeEpisodeMarker(name) ?? (Tokens(name).Any(IsEpisodeMarker) ? null : Parse(name));

    private static ArtworkTitle? ParseBeforeEpisodeMarker(string? name)
    {
        var tokens = Tokens(name);
        var marker = tokens.ToList().FindIndex(token => EpisodeMarker().IsMatch(token));
        return marker > 0 ? Parse(string.Join(' ', tokens.Take(marker))) : null;
    }

    private static bool IsEpisodeMarker(string token) => EpisodeMarker().IsMatch(token);

    private static bool IsSeasonFolder(string folder) => Tokens(folder) is [var first, ..] && SeasonFolder().IsMatch(first);

    private static IReadOnlyList<string> Tokens(string? name) =>
        string.IsNullOrWhiteSpace(name) ? [] : ProcessingDomain.TokenizeNormalized(ProcessingDomain.NormalizeTitleish(name));

    private static ArtworkTitle? Parse(string? name) =>
        ReleaseTitle.Parse(name) is { } parsed ? new ArtworkTitle(parsed.Title, parsed.Year) : null;
}
