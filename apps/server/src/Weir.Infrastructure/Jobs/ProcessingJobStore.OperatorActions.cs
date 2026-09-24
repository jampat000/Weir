using System.Globalization;
using Weir.Core.Jobs;
using Weir.Core.Time;

namespace Weir.Infrastructure.Jobs;

/// <summary>Operator actions on a job row: recover, cancel, reprioritise.</summary>
public sealed partial class ProcessingJobStore
{
    /// <summary>
    /// Operator recovery that marks a <c>handler_ok_finalize_failed</c> row completed without re-running
    /// the handler.
    /// </summary>
    public Task<JobActionOutcome> RecoverHandlerOkFinalizeFailedToCompletedAsync(
        long jobId,
        string recoveredByLabel,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        var when = now ?? _time.GetUtcNow();
        return InTransactionAsync(
            (connection, transaction) =>
            {
                var job = Get(connection, transaction, jobId);
                if (job is null)
                {
                    return JobActionOutcome.NotFound;
                }

                if (job.Status != ProcessingJobStatus.HandlerOkFinalizeFailed)
                {
                    return JobActionOutcome.WrongStatus;
                }

                Execute(
                    connection,
                    transaction,
                    "UPDATE jobs SET last_error = @error, status = @status, lease_owner = NULL, lease_expires_at = NULL, " +
                    "updated_at = CURRENT_TIMESTAMP WHERE id = @id",
                    ("@error", JobQueueRules.RecoveredFinalizeFailureError(job.LastError, PyDateTime.TruncateToMicroseconds(when.ToUniversalTime()), recoveredByLabel)),
                    ("@status", ProcessingJobStatus.Completed),
                    ("@id", jobId));
                _metrics.RecordJobEvent(MetricsModule, "completed");
                RecordQueueDepth(connection, transaction);
                return JobActionOutcome.Ok;
            },
            cancellationToken);
    }

    /// <summary>
    /// Cancel a pending row (only pending rows). The dedupe key becomes a tombstone so a later enqueue may
    /// reuse it.
    /// </summary>
    public Task<JobActionOutcome> CancelPendingAsync(long jobId, CancellationToken cancellationToken = default) =>
        InTransactionAsync(
            (connection, transaction) =>
            {
                var job = Get(connection, transaction, jobId);
                if (job is null)
                {
                    return JobActionOutcome.NotFound;
                }

                if (job.Status != ProcessingJobStatus.Pending)
                {
                    return JobActionOutcome.WrongStatus;
                }

                Execute(
                    connection,
                    transaction,
                    "UPDATE jobs SET dedupe_key = @dedupe, status = @status, lease_owner = NULL, lease_expires_at = NULL, " +
                    "last_error = @error, updated_at = CURRENT_TIMESTAMP WHERE id = @id",
                    ("@dedupe", JobQueueRules.TombstoneCancelledDedupeKey(job.DedupeKey, job.Id)),
                    ("@status", ProcessingJobStatus.Cancelled),
                    ("@error", JobQueueRules.CancelledByOperatorError),
                    ("@id", jobId));
                return JobActionOutcome.Ok;
            },
            cancellationToken);

    /// <summary>Raise one pending job above everything else waiting.</summary>
    public Task<JobActionOutcome> MoveToTopAsync(long jobId, CancellationToken cancellationToken = default) =>
        InTransactionAsync(
            (connection, transaction) =>
            {
                var job = Get(connection, transaction, jobId);
                if (job is null)
                {
                    return JobActionOutcome.NotFound;
                }

                if (job.Status != ProcessingJobStatus.Pending)
                {
                    return JobActionOutcome.WrongStatus;
                }

                var highest = Scalar(connection, transaction, "SELECT max(priority) FROM jobs WHERE status = @pending", ("@pending", ProcessingJobStatus.Pending));
                var priority = (highest is null or DBNull ? 0 : Convert.ToInt64(highest, CultureInfo.InvariantCulture)) + 1;
                if (priority != job.Priority)
                {
                    Execute(
                        connection,
                        transaction,
                        "UPDATE jobs SET priority = @priority, updated_at = CURRENT_TIMESTAMP WHERE id = @id",
                        ("@priority", priority),
                        ("@id", jobId));
                }

                return JobActionOutcome.Ok;
            },
            cancellationToken);
}
