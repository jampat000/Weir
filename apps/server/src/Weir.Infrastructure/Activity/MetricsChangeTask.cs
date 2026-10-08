using Weir.Core.Metrics;
using Weir.Infrastructure.Scheduling;

namespace Weir.Infrastructure.Activity;

/// <summary>
/// <c>metrics-change</c>: tells the open screens that Weir's request and failure counters moved
/// (<see cref="DataTopics.Metrics"/>), at most once per interval however many requests came in. The counters change with
/// every request, so announcing each one would send a frame per request; this sends one per tick. It does nothing while no
/// stream is open, since a screen that opens later reads the counters fresh.
/// </summary>
public sealed class MetricsChangeTask : IPeriodicTask
{
    private readonly RuntimeMetricsStore _metrics;
    private readonly DataChangePublisher _changes;
    private long _announced;

    public MetricsChangeTask(RuntimeMetricsStore metrics, DataChangePublisher changes)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
    }

    public string Name => "metrics-change";

    public string? Label => null;

    public TimeSpan Interval => TimeSpan.FromSeconds(5);

    public bool RunAtStart => false;

    public TimeSpan? FailureCooldown => null;

    public string FailureMessage => "Weir could not tell open screens that its counters changed.";

    public Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!_changes.HasListeners)
        {
            return Task.CompletedTask;
        }

        var stamp = _metrics.ChangeStamp();
        if (stamp != _announced)
        {
            _announced = stamp;
            _changes.Publish(DataTopics.Metrics);
        }

        return Task.CompletedTask;
    }
}
