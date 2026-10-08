using Microsoft.Data.Sqlite;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Jobs;

/// <summary>Tells the live stream when the queue changes, so the screens that show it need no timer.</summary>
public sealed partial class ProcessingJobStore
{
    /// <summary>
    /// Says that the queue changed for a job of <paramref name="jobKind"/> once <paramref name="uow"/> commits, for a unit of
    /// work that writes job rows itself rather than through <see cref="InTransactionAsync{T}"/>.
    /// </summary>
    public void AnnounceOnCommit(UnitOfWork uow, string jobKind)
    {
        ArgumentNullException.ThrowIfNull(uow);
        _changes?.PublishQueueChangeOnCommit(uow, jobKind);
    }

    /// <summary>Says that old job rows were removed, for a prune that commits in batches of its own.</summary>
    internal void AnnouncePruned() => _changes?.Publish(DataTopics.Jobs);

    internal void AnnounceQueueChange(SqliteTransaction transaction, string jobKind)
    {
        if (UnitOfWork.OwnerOf(transaction) is { } unit)
        {
            AnnounceOnCommit(unit, jobKind);
        }
    }
}
