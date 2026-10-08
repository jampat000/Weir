using Weir.Core.Jobs;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Jobs;

/// <summary>Which data topics a change to the job queue moves, so every place that changes it says the same thing.</summary>
internal static class QueueChangeTopics
{
    /// <summary>
    /// Says, once <paramref name="uow"/> commits, that a job of <paramref name="jobKind"/> was queued, started, finished, put
    /// back, raised or cancelled. A unit of work that changes several jobs says each topic once. File work moves what "Files at
    /// once" reads out; a maintenance sweep moves the maintenance panel.
    /// </summary>
    public static void PublishQueueChangeOnCommit(this DataChangePublisher changes, UnitOfWork uow, string jobKind)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var topic in TopicsMovedBy(jobKind))
        {
            changes.PublishOnCommit(uow, topic);
        }
    }

    private static IEnumerable<string> TopicsMovedBy(string jobKind)
    {
        yield return DataTopics.Jobs;
        if (WorkLanes.LaneOf(jobKind) == WorkLane.Files)
        {
            yield return DataTopics.FilesAtOnce;
        }

        if (PeriodicJobKinds.IsMaintenance(jobKind))
        {
            yield return DataTopics.Maintenance;
        }
    }
}
