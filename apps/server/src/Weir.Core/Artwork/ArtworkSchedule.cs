namespace Weir.Core.Artwork;

/// <summary>When a poster lookup that did not succeed may be tried again.</summary>
public static class ArtworkSchedule
{
    /// <summary>How long a title the service does not know is left alone before it is asked about again.</summary>
    public static readonly TimeSpan MissingRetryAfter = TimeSpan.FromDays(7);

    /// <summary>How long a title waits after the service could not be reached for it, before its first retry.</summary>
    public static readonly TimeSpan FirstFailureBackoff = TimeSpan.FromMinutes(5);

    /// <summary>The longest a title waits between retries after the service could not be reached.</summary>
    public static readonly TimeSpan LongestFailureBackoff = TimeSpan.FromHours(6);

    /// <summary>How long every lookup pauses when the service answers that it is busy and does not say for how long.</summary>
    public static readonly TimeSpan DefaultBusyPause = TimeSpan.FromMinutes(1);

    /// <summary>The longest Weir honours a busy answer's own request to wait.</summary>
    public static readonly TimeSpan LongestBusyPause = TimeSpan.FromHours(1);

    /// <summary>How long every lookup pauses after the service could not be reached at all.</summary>
    public static readonly TimeSpan UnreachablePause = TimeSpan.FromMinutes(1);

    /// <summary>The wait before the retry that follows <paramref name="failures"/> failed attempts, doubling each time.</summary>
    public static TimeSpan FailureBackoff(int failures)
    {
        var doublings = Math.Clamp(failures - 1, 0, 16);
        var wait = FirstFailureBackoff * Math.Pow(2, doublings);
        return wait < LongestFailureBackoff ? wait : LongestFailureBackoff;
    }
}
