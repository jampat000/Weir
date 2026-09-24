namespace Weir.Infrastructure.Jobs;

/// <summary>Queue counters for metrics: job events and queue depth per module.</summary>
public interface IJobQueueMetrics
{
    /// <summary><paramref name="jobEvent"/> is <c>started</c>, <c>completed</c> or <c>failed</c>.</summary>
    void RecordJobEvent(string moduleName, string jobEvent);

    void SetQueueDepth(string moduleName, int depth);
}

/// <summary>Discards queue counters when no metrics sink is configured.</summary>
public sealed class NoJobQueueMetrics : IJobQueueMetrics
{
    public static NoJobQueueMetrics Instance { get; } = new();

    public void RecordJobEvent(string moduleName, string jobEvent)
    {
    }

    public void SetQueueDepth(string moduleName, int depth)
    {
    }
}
