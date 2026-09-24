using Weir.Core.Jobs;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>Dropping a hand-off that has not started, and settling one after a queued job of it was cancelled elsewhere.</summary>
public sealed partial class HandoffLedgerStore
{
    /// <summary>
    /// Drop a hand-off that has not started. Never touches a file and never stops running work.
    /// </summary>
    public async Task<(bool Cancelled, string Sentence)> CancelAsync(UnitOfWork uow, ProcessingJobStore jobs, HandoffLedgerRow row)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(row);
        var status = await CurrentStatusAsync(uow, row).ConfigureAwait(false);
        if (status.State is not (HandoffLedgerRules.Queued or HandoffLedgerRules.Scheduled))
        {
            return (false, IntakeRules.RefusedCancelSentence(status.State));
        }

        foreach (var job in await JobsForAsync(uow, row).ConfigureAwait(false))
        {
            if (job.Status != ProcessingJobStatus.Pending)
            {
                continue;
            }

            await uow.ExecuteAsync(
                "UPDATE jobs SET dedupe_key = $dedupe, status = $status, lease_owner = NULL, lease_expires_at = NULL, " +
                "last_error = $error, updated_at = CURRENT_TIMESTAMP WHERE id = $id",
                ("$dedupe", JobQueueRules.TombstoneCancelledDedupeKey(job.DedupeKey, job.Id)),
                ("$status", ProcessingJobStatus.Cancelled),
                ("$error", JobQueueRules.CancelledByOperatorError),
                ("$id", job.Id)).ConfigureAwait(false);
        }

        // Its files read cancelled too (#643), so the Files list agrees with the manager and a scan does not quietly queue
        // them again. A file that already has an outcome from an earlier hand-off keeps it.
        if (row.LibraryId is { } libraryId)
        {
            foreach (var file in await FileRowsAsync(uow, row).ConfigureAwait(false))
            {
                await _files.MarkCancelledAsync(uow, libraryId, file.RelativePath, CancelledFileReasons.ByManager).ConfigureAwait(false);
            }
        }

        await uow.ExecuteAsync(
            "UPDATE media_manager_handoffs SET state = $state, message = $message, last_changed_at = $now WHERE id = $row",
            ("$state", HandoffLedgerRules.Cancelled),
            ("$message", HandoffLedgerRules.CancelledMessage),
            ("$now", TimestampColumns.Orm(_time.GetUtcNow())),
            ("$row", row.Id)).ConfigureAwait(false);
        return (true, HandoffLedgerRules.CancelledMessage);
    }

    /// <summary>
    /// After one of this hand-off's queued passes was cancelled in Weir (#643): once nothing of the hand-off is left to run,
    /// it ends, and the manager hears it instead of "queued" for ever. It ends cancelled when nothing was delivered for it
    /// since <paramref name="handedOverAt"/>. A file processed for an earlier hand-off of the same path does not count.
    /// A hand-off with other files still queued or working goes on, and so does a pack with files already delivered, which
    /// then reads as its files do. True when it ended cancelled.
    /// </summary>
    public async Task<bool> SettleAfterJobCancelledAsync(UnitOfWork uow, HandoffLedgerRow row, DateTimeOffset handedOverAt, string message)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(row);
        if (HandoffLedgerRules.TerminalStates.Contains(row.State))
        {
            return false;
        }

        var jobs = await JobsForAsync(uow, row).ConfigureAwait(false);
        if (jobs.Any(job => job.Status is ProcessingJobStatus.Pending or ProcessingJobStatus.Leased))
        {
            return false;
        }

        var files = await FileRowsAsync(uow, row).ConfigureAwait(false);
        var deliveredSince = files.Any(file =>
            (file.Status is ProcessingFileStatuses.Processed or ProcessingFileStatuses.PassedThrough or ProcessingFileStatuses.Rejected) &&
            file.UpdatedAt is { } changed && changed >= handedOverAt);
        if (deliveredSince)
        {
            await CurrentStatusAsync(uow, row).ConfigureAwait(false);
            return false;
        }

        await uow.ExecuteAsync(
            "UPDATE media_manager_handoffs SET state = $state, output_path = NULL, message = $message, last_changed_at = $now WHERE id = $row",
            ("$state", HandoffLedgerRules.Cancelled),
            ("$message", message),
            ("$now", TimestampColumns.Orm(_time.GetUtcNow())),
            ("$row", row.Id)).ConfigureAwait(false);
        return true;
    }
}
