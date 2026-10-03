using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Weir.Core.Processing;

namespace Weir.Core.Artwork;

/// <summary>
/// The identities poster lookups are keyed by. A lookup key names one title, so a title is asked about once however many
/// files it has; a poster id names one image, so the same image is stored once however many titles use it.
/// </summary>
public static partial class ArtworkKeys
{
    private const int PosterIdBytes = 16;

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex PosterIdPattern();

    /// <summary>The key for a title whose TMDb id the media manager supplied.</summary>
    public static string ForTmdbId(string mediaScope, long tmdbId) =>
        string.Create(CultureInfo.InvariantCulture, $"tmdb:{mediaScope}:{tmdbId}");

    /// <summary>The key for a title known only by a name and year, so the same words in any letter case or punctuation share one lookup.</summary>
    public static string ForTitle(string mediaScope, ArtworkTitle title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return string.Create(CultureInfo.InvariantCulture, $"title:{mediaScope}:{ProcessingDomain.NormalizeTitleish(title.Title)}:{title.Year ?? 0}");
    }

    /// <summary>The key for a poster a media manager named for a title it did not name.</summary>
    public static string ForPosterRef(string posterRef) => "poster:" + posterRef;

    /// <summary>The opaque, stable id a poster is served under: derived from the reference to where the image comes from.</summary>
    public static string PosterId(string posterRef)
    {
        ArgumentException.ThrowIfNullOrEmpty(posterRef);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("weir-poster:" + posterRef));
        return Convert.ToHexStringLower(hash.AsSpan(0, PosterIdBytes));
    }

    /// <summary>Whether <paramref name="value"/> has the shape of an id <see cref="PosterId"/> makes, so it is safe to look up and to name a file by.</summary>
    public static bool IsPosterId(string? value) => value is not null && PosterIdPattern().IsMatch(value);
}
