using Weir.Core.Artwork;

namespace Weir.Infrastructure.Artwork;

/// <summary>
/// Weir's own limit on how hard it asks the metadata service, well inside the service's budget (120 calls a minute per address,
/// shared with every other app on the network): at most one search every two seconds, and none at all while the service has asked
/// callers to wait or could not be reached.
/// </summary>
public sealed class ArtworkRateLimiter
{
    /// <summary>The least time between two searches: 30 a minute.</summary>
    public static readonly TimeSpan MinimumBetweenSearches = TimeSpan.FromSeconds(2);

    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private DateTimeOffset _nextSearchAt = DateTimeOffset.MinValue;
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;

    public ArtworkRateLimiter(TimeProvider time)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>Whether lookups are paused: the service asked to be left alone, or could not be reached.</summary>
    public bool IsPaused
    {
        get
        {
            lock (_lock)
            {
                return _time.GetUtcNow() < _pausedUntil;
            }
        }
    }

    /// <summary>The service said it is busy. Pause for as long as it asked, within reason, or a minute when it did not say.</summary>
    public void HonourBusy(TimeSpan? retryAfter) =>
        PauseFor(retryAfter is { } asked && asked > TimeSpan.Zero ? TimeSpan.FromTicks(Math.Min(asked.Ticks, ArtworkSchedule.LongestBusyPause.Ticks)) : ArtworkSchedule.DefaultBusyPause);

    /// <summary>The service could not be reached. Pause so an outage costs a few attempts, not one per title.</summary>
    public void HonourUnreachable() => PauseFor(ArtworkSchedule.UnreachablePause);

    /// <summary>Wait for this caller's turn to search.</summary>
    public async Task WaitForSearchAsync(CancellationToken cancellationToken)
    {
        TimeSpan wait;
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            var turn = new[] { now, _nextSearchAt, _pausedUntil }.Max();
            _nextSearchAt = turn + MinimumBetweenSearches;
            wait = turn - now;
        }

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    private void PauseFor(TimeSpan duration)
    {
        lock (_lock)
        {
            var until = _time.GetUtcNow() + duration;
            if (until > _pausedUntil)
            {
                _pausedUntil = until;
            }
        }
    }
}
