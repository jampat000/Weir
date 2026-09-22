using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Core.Processing.RemuxPass;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>What cancelling one queued job did: whether it could, and the hand-off it ended, if any.</summary>
public sealed record PendingJobCancelResult(JobActionOutcome Outcome, HandoffLedgerRow? EndedHandoff = null);

/// <summary>
/// Cancelling one queued job from the Jobs screen (<c>POST /processing/jobs/{id}/cancel-pending</c>). Only a pending job can
/// be cancelled, and its dedupe key becomes a tombstone so a later enqueue may reuse it.
/// <para>
/// #643: this used to change the job row and nothing else. The file then read "Waiting" for ever, or the next scan queued it
/// again. A hand-off the job belonged to kept answering "queued" to the media manager, which could never end it. Now a
/// remux pass's file reads cancelled, and a hand-off with nothing else left to run ends the way the manager's own cancel ends
/// it. All of this happens in the caller's unit of work, in one transaction.
/// </para>
/// </summary>
public static class PendingJobCancellation
{
    public static async Task<PendingJobCancelResult> CancelAsync(UnitOfWork uow, HandoffLedgerStore ledger, long jobId)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(ledger);
        var found = await uow.QuerySingleAsync(
            "SELECT id, dedupe_key, job_kind, payload_json, status, created_at FROM jobs WHERE id = $id",
            reader => new PendingJobRow(
                SqliteValues.GetInt64(reader, 0),
                SqliteValues.GetString(reader, 1),
                SqliteValues.GetString(reader, 2),
                SqliteValues.GetStringOrNull(reader, 3),
                SqliteValues.GetString(reader, 4),
                PythonTimestamps.Parse(reader.GetValue(5))),
            ("$id", jobId)).ConfigureAwait(false);
        if (found is null)
        {
            return new PendingJobCancelResult(JobActionOutcome.NotFound);
        }

        if (found.Status != ProcessingJobStatus.Pending)
        {
            return new PendingJobCancelResult(JobActionOutcome.WrongStatus);
        }

        await uow.ExecuteAsync(
            "UPDATE jobs SET dedupe_key = $dedupe, status = $status, lease_owner = NULL, lease_expires_at = NULL, " +
            "last_error = $error, updated_at = CURRENT_TIMESTAMP WHERE id = $id",
            ("$dedupe", JobQueueRules.TombstoneCancelledDedupeKey(found.DedupeKey, found.Id)),
            ("$status", ProcessingJobStatus.Cancelled),
            ("$error", JobQueueRules.CancelledByOperatorError),
            ("$id", found.Id)).ConfigureAwait(false);

        var payload = Parse(found.Payload);
        if (found.Kind == RemuxPassOutcomes.JobKind &&
            payload?.Get("library_id") is PyInt library && library.Value > 0 &&
            payload.Get("relative_media_path") is PyStr { Value.Length: > 0 } path)
        {
            await FileStateStore.MarkCancelledAsync(uow, (long)library.Value, path.Value, CancelledFileReasons.InWeir).ConfigureAwait(false);
        }

        if (HandoffOrigin.FromPayload(payload) is { HandoffId: { Length: > 0 } handoffId } origin)
        {
            var handoff = await HandoffLedgerStore.FindAsync(uow, origin.SourceKey, handoffId).ConfigureAwait(false);
            if (handoff is not null &&
                await ledger.SettleAfterJobCancelledAsync(
                    uow, handoff, found.CreatedAt ?? DateTimeOffset.MinValue, HandoffLedgerRules.CancelledInWeirMessage).ConfigureAwait(false))
            {
                return new PendingJobCancelResult(JobActionOutcome.Ok, handoff);
            }
        }

        return new PendingJobCancelResult(JobActionOutcome.Ok);
    }

    private sealed record PendingJobRow(long Id, string DedupeKey, string Kind, string? Payload, string Status, DateTimeOffset? CreatedAt);

    private static PyDict? Parse(string? payloadJson)
    {
        if (string.IsNullOrEmpty(payloadJson))
        {
            return null;
        }

        try
        {
            return PyJsonParser.Parse(payloadJson) as PyDict;
        }
        catch (PyJsonDecodeException)
        {
            return null;
        }
    }
}
