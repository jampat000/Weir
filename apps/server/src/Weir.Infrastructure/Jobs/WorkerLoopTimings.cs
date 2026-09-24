namespace Weir.Infrastructure.Jobs;

/// <summary>Worker loop timings, adjustable for tests.</summary>
public sealed record WorkerLoopTimings
{
    /// <summary>How long an idle slot waits before looking for work again.</summary>
    public TimeSpan IdleSleep { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a slot waits after a crashed tick.</summary>
    public TimeSpan TickErrorBackoff { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How long a slot trusts the saved files-at-once value.</summary>
    public TimeSpan ConcurrencyCacheTtl { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a claim leases a job, in seconds.</summary>
    public int LeaseSeconds { get; init; } = ProcessingJobProcessor.DefaultLeaseSeconds;
}
