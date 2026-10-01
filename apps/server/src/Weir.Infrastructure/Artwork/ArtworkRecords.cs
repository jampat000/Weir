namespace Weir.Infrastructure.Artwork;

/// <summary>One title to find a poster for, with whatever a media manager said about it.</summary>
public sealed record ArtworkLookupRequest(
    string Key,
    string MediaScope,
    string Title,
    int? Year,
    long? TmdbId,
    long? TvdbId,
    string? ImdbId,
    string? PosterRef,
    int Priority);

/// <summary>A queued lookup as the resolver reads it.</summary>
public sealed record ArtworkLookup(string Key, string MediaScope, string Title, int? Year, long? TmdbId, long? TvdbId, string? PosterRef, int Attempts);

/// <summary>A stored poster image.</summary>
public sealed record StoredPoster(string PosterId, string ContentType);

/// <summary>A file whose title has not been read yet, with the name its manager gave the title when it knows one.</summary>
public sealed record ArtworkCandidate(long LibraryId, string Path, string MediaScope, string? KnownTitle);

/// <summary>How soon a lookup is attempted relative to the others.</summary>
public static class ArtworkPriority
{
    /// <summary>Files from a library scan: a whole library can queue thousands, so they wait for the rest.</summary>
    public const int Library = 0;

    /// <summary>A file Weir is processing or a manager handed over.</summary>
    public const int Processing = 1;
}

/// <summary>The states a lookup is in.</summary>
public static class ArtworkOutcomes
{
    public const string Pending = "pending";
    public const string Found = "found";
    public const string Missing = "missing";
}
