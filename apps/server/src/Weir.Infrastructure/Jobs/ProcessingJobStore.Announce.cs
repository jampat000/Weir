using Microsoft.Data.Sqlite;
using Weir.Core.Jobs;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Jobs;

/// <summary>Tells the live stream when the queue changes, so the screens that show it need no timer.</summary>
public sealed partial class ProcessingJobStore
{
    private const string AnnouncedKeyPrefix = "weir_queue_change_announced:";

    /// <summary>
    /// Announces that a job of <paramref name="jobKind"/> was queued, started, finished, put back or cancelled, once the
    /// transaction behind <paramref name="transaction"/> commits. A unit of work that changes several jobs announces each
    /// topic once. File work moves what "Files at once" reads out; a maintenance sweep moves the maintenance panel.
    /// </summary>
    private void AnnounceQueueChange(SqliteTransaction transaction, string jobKind)
    {
        if (_changes is null || UnitOfWork.OwnerOf(transaction) is not { } unit)
        {
            return;
        }

        foreach (var topic in TopicsMovedBy(jobKind))
        {
            if (unit.Items.TryAdd(AnnouncedKeyPrefix + topic, true))
            {
                unit.OnCommitted(() => _changes.Publish(topic));
            }
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
