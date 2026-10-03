using System.Text.RegularExpressions;

namespace Weir.Core.Artwork;

/// <summary>
/// Where poster images may come from. A poster is named by a reference: a TMDb image file name, fetched through the
/// metadata gateway at Weir's own size, or an absolute https address on one of the other image hosts Deluno takes posters from. Weir fetches nothing else, so
/// an address in a payload can never make it contact a host of the sender's choosing.
/// </summary>
public static partial class ArtworkPosterSource
{
    /// <summary>The address Deluno's metadata service answers on.</summary>
    public const string DefaultGatewayUrl = "https://deluno-metadata-gateway.ejmdigital.workers.dev";

    /// <summary>The size Weir shows TMDb posters at.</summary>
    public const string PosterSize = "w342";

    /// <summary>TMDb's image host.</summary>
    public const string TmdbImageHost = "image.tmdb.org";

    /// <summary>TheTVDB's artwork host.</summary>
    public const string TvdbArtworkHost = "artworks.thetvdb.com";

    /// <summary>The image host of Deluno's OMDb fallback.</summary>
    public const string OmdbImageHost = "m.media-amazon.com";

    /// <summary>The hosts a poster address in a hand-off may be on, and the only ones Weir fetches an image from.</summary>
    public static readonly IReadOnlyList<string> ImageHosts = [new Uri(DefaultGatewayUrl).Host, TmdbImageHost, TvdbArtworkHost, OmdbImageHost];

    [GeneratedRegex(@"^[A-Za-z0-9_-]{4,100}\.(jpg|jpeg|png|webp)$")]
    private static partial Regex TmdbFileName();

    [GeneratedRegex(@"^/[A-Za-z0-9_./@-]{1,300}\.(jpg|jpeg|png|webp)$")]
    private static partial Regex RemoteImagePath();

    /// <summary>
    /// The reference for the poster address in an answer from the gateway at <paramref name="gatewayHost"/> (which may be
    /// a stand-in on plain http), or null when the address is not on a host Weir fetches images from.
    /// </summary>
    public static string? RefFromAnswerUrl(string? url, string gatewayHost) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Equals(gatewayHost, StringComparison.OrdinalIgnoreCase) && uri.Scheme is "http" or "https"
            ? TmdbRef(uri)
            : RefFromHandoffUrl(url);

    /// <summary>The reference for a poster address from a hand-off: absolute https on one of <see cref="ImageHosts"/>, otherwise null.</summary>
    public static string? RefFromHandoffUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !ImageHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.Host.Equals(TvdbArtworkHost, StringComparison.OrdinalIgnoreCase) || uri.Host.Equals(OmdbImageHost, StringComparison.OrdinalIgnoreCase)
            ? RemoteRef(uri)
            : TmdbRef(uri);
    }

    /// <summary>Where a reference is fetched from: the gateway's own route for a TMDb file, or the address itself for any other.</summary>
    public static string ImageUrl(string gatewayUrl, string posterRef) =>
        posterRef.StartsWith("https://", StringComparison.Ordinal) ? posterRef : $"{gatewayUrl}/artwork/{PosterSize}/{Uri.EscapeDataString(posterRef)}";

    private static string? TmdbRef(Uri uri)
    {
        var name = uri.Segments[^1];
        return TmdbFileName().IsMatch(name) ? name : null;
    }

    private static string? RemoteRef(Uri uri) =>
        uri.Query.Length == 0 && RemoteImagePath().IsMatch(uri.AbsolutePath) ? $"https://{uri.Host}{uri.AbsolutePath}" : null;
}
