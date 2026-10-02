namespace Weir.Infrastructure.Artwork;

/// <summary>How the metadata gateway answered.</summary>
public enum GatewayStatus
{
    /// <summary>It answered with what was asked for.</summary>
    Ok,

    /// <summary>It does not know the title or image, or refused a request that asking again cannot fix.</summary>
    NotFound,

    /// <summary>It is over its limit and asked callers to wait (<see cref="GatewayAnswer{T}.RetryAfter"/>).</summary>
    Busy,

    /// <summary>It could not be reached, or answered with an error of its own.</summary>
    Unavailable,
}

/// <summary>The gateway's answer, with what it carried when <see cref="Status"/> is <see cref="GatewayStatus.Ok"/>.</summary>
public sealed record GatewayAnswer<T>(GatewayStatus Status, T? Value = default, TimeSpan? RetryAfter = null)
    where T : class;

/// <summary>Makes <see cref="GatewayAnswer{T}"/> values.</summary>
public static class GatewayAnswers
{
    public static GatewayAnswer<T> Of<T>(T value)
        where T : class => new(GatewayStatus.Ok, value);

    public static GatewayAnswer<T> NotFound<T>()
        where T : class => new(GatewayStatus.NotFound);

    public static GatewayAnswer<T> Unavailable<T>()
        where T : class => new(GatewayStatus.Unavailable);

    public static GatewayAnswer<T> Busy<T>(TimeSpan? retryAfter)
        where T : class => new(GatewayStatus.Busy, RetryAfter: retryAfter);
}

/// <summary>
/// The best match a search found: a reference to the poster image when the match has one, the title's TMDb id when the answer gave one,
/// and the original language it reported (null when it reported none).
/// </summary>
public sealed record GatewayMatch(string? PosterRef, long? TmdbId, string? OriginalLanguage);

/// <summary>What the gateway sent back: the bytes and their content type.</summary>
public sealed record GatewayBody(byte[] Bytes, string ContentType);
