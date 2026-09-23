using System.Collections.Concurrent;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// When each periodic family next runs and how often, as its timer last worked it out, so Settings › Cleanup shows a
/// real next run instead of a guess. A family that is switched off has no entry.
/// </summary>
public sealed class PeriodicEnqueueClock
{
    private readonly ConcurrentDictionary<string, (string JobKind, DateTimeOffset NextRunAt, TimeSpan Interval)> _byTimer =
        new(StringComparer.Ordinal);

    /// <summary>The timer named <paramref name="timer"/> runs <paramref name="jobKind"/> next at <paramref name="nextRunAt"/>.</summary>
    public void Record(string timer, string jobKind, DateTimeOffset nextRunAt, TimeSpan interval) =>
        _byTimer[timer] = (jobKind, nextRunAt, interval);

    /// <summary>The timer named <paramref name="timer"/> is switched off.</summary>
    public void Forget(string timer) => _byTimer.TryRemove(timer, out _);

    /// <summary>The soonest next run of any timer for these job kinds, and its interval; null when none is running.</summary>
    public (DateTimeOffset NextRunAt, TimeSpan Interval)? NextRunFor(IEnumerable<string> jobKinds)
    {
        ArgumentNullException.ThrowIfNull(jobKinds);
        var kinds = jobKinds.ToHashSet(StringComparer.Ordinal);
        (DateTimeOffset, TimeSpan)? soonest = null;
        foreach (var (kind, at, interval) in _byTimer.Values)
        {
            if (kinds.Contains(kind) && (soonest is null || at < soonest.Value.Item1))
            {
                soonest = (at, interval);
            }
        }

        return soonest;
    }
}
