namespace Weir.Infrastructure.Jobs;

/// <summary>One family's periodic enqueue timer.</summary>
public interface IPeriodicEnqueuer
{
    /// <summary>A name for logs.</summary>
    string Name { get; }

    /// <summary>The job kind it enqueues; the timer runs only when this server has a handler for it.</summary>
    string JobKind { get; }

    /// <summary>The interval the environment gives: what <see cref="IntervalAsync"/> falls back to.</summary>
    TimeSpan Interval { get; }

    /// <summary>
    /// Whether the family is switched on. Read on every check, so switching a family on or off in Settings › Cleanup
    /// applies within <see cref="PeriodicEnqueueService.DefaultRecheck"/>, without a restart.
    /// </summary>
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken);

    /// <summary>How often the family runs now: the interval saved in Settings › Cleanup, else <see cref="Interval"/>.</summary>
    Task<TimeSpan> IntervalAsync(CancellationToken cancellationToken) => Task.FromResult(Interval);

    Task EnqueueOnceAsync(CancellationToken cancellationToken);
}
