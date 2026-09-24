using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Core.Processing.RemuxPass;
using Weir.Core.Time;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.MediaManagers;

namespace Weir.Infrastructure.Processing.RemuxPass;

public sealed partial class RemuxPassHandler
{
    /// <summary>Keeps the durable Files row in step with the result shown in Activity.</summary>
    internal async Task ApplyFileOutcomeStateAsync(PyDict result, long? libraryId, string mediaScope, PyDict? origin)
    {
        if (result.Get("relative_media_path") is not PyStr relValue || PyStrings.Strip(relValue.Value).Length == 0)
        {
            return;
        }

        var rel = PyStrings.Strip(relValue.Value);
        var updates = new PyDict();
        try
        {
            await LockedWrites.RunAsync(
                _database,
                async uow =>
                {
                    updates = new PyDict();
                    var library = await ResolveLibraryAsync(uow, libraryId, mediaScope).ConfigureAwait(false);
                    if (library is null)
                    {
                        return;
                    }

                    var now = _time.GetUtcNow();
                    if (result.Get("rejection_kind") is { IsTruthy: true })
                    {
                        var rejectionReason = PyStrings.Slice(CollapseWhitespace(TextOr(result.Get("reason"), "Weir rejected this file before processing.")), 1200);
                        var cleanupDetail = TextOr(result.Get("rejected_cleanup_detail"), "The saved rejected-file action has not run yet.");
                        if (await _failurePolicy.RejectBadReleaseAsync(uow, library, rel, rejectionReason, origin).ConfigureAwait(false))
                        {
                            updates.Set("reject_queued", true);
                            cleanupDetail = "Weir is telling your media manager this release is bad so it can find a different one, " +
                                            "and removes the download only once the manager accepts.";
                        }

                        var reason = $"{rejectionReason} {cleanupDetail}";
                        if (await RemuxPassFileState.MarkFileStatusAsync(uow, library.Id, rel, ProcessingFileStatuses.Skipped, reason, now).ConfigureAwait(false))
                        {
                            await RemuxPassFileState.ClearFailureFieldsAsync(uow, library.Id, rel).ConfigureAwait(false);
                        }

                        updates.Set("retry_scheduled", false).Set("quarantined", false).Set("failure_next_retry_at", PyNull.Instance).Set("failure_operator_message", reason);
                        return;
                    }

                    if (result.Get("retryable_wait") is PyBool { Value: true })
                    {
                        var reason = PyStrings.Slice(CollapseWhitespace(TextOr(result.Get("reason"), "Weir is waiting until no other program is writing this file.")), 1200);
                        if (await RemuxPassFileState.MarkFileStatusAsync(uow, library.Id, rel, ProcessingFileStatuses.OnHold, reason, now).ConfigureAwait(false))
                        {
                            await RemuxPassFileState.ClearFailureFieldsAsync(uow, library.Id, rel).ConfigureAwait(false);
                            // Another look is booked (#646): the file is on hold until it, not ready for the next free lane.
                            if (result.Get("failure_next_retry_at") is PyStr { Value.Length: > 0 } booked &&
                                PythonTimestamps.Parse(booked.Value) is { } lookAgainAt)
                            {
                                await uow.ExecuteAsync(
                                    "UPDATE files SET hold_until = $hold WHERE library_id = $library AND relative_path = $path",
                                    ("$hold", PythonTimestamps.Orm(lookAgainAt)),
                                    ("$library", library.Id),
                                    ("$path", rel)).ConfigureAwait(false);
                            }
                        }

                        updates.Set("retry_scheduled", false).Set("quarantined", false).Set("failure_next_retry_at", PyNull.Instance).Set("failure_operator_message", reason);
                        return;
                    }

                    if (result.Get("ok") is PyBool { Value: false })
                    {
                        var failureClass = ProcessingFailureClasses.Classify(TextOr(result.Get("outcome"), string.Empty));
                        var reason = PyStrings.Slice(CollapseWhitespace(TextOr(result.Get("reason"), "Weir returned an unsuccessful result.")), 1200);
                        var decision = await RemuxPassFileState.RecordFailureAsync(uow, library, rel, failureClass, reason, now).ConfigureAwait(false);
                        var followUp = await _failurePolicy.ApplyFailurePolicyAsync(
                            uow, library, rel, decision.WillRetry, origin, result.Get("content_unusable") is PyBool { Value: true }).ConfigureAwait(false);
                        updates
                            .Set("pass_through_queued", followUp == ProcessingFailurePolicies.PassThrough)
                            .Set("reject_queued", followUp == ProcessingFailurePolicies.Reject)
                            .Set("failure_class", failureClass)
                            .Set("retry_scheduled", decision.WillRetry)
                            .Set("quarantined", decision.Quarantined)
                            .Set("failure_next_retry_at", decision.NextRetryAt is { } next ? PyDateTime.FromDateTimeOffset(next).IsoFormat() : null)
                            .Set("failure_operator_message", decision.Reason);
                        return;
                    }

                    var processedReason = result.Get("pass_through_unchanged") is PyBool { Value: true }
                        ? "Weir passed this file through unchanged at the operator's request and placed it in the output folder."
                        : result.Get("outcome") is PyStr { Value: RemuxPassOutcomes.LiveSkippedNotRequired }
                            ? "Checked this file and found that no changes were needed."
                            : "Finished processing this file.";
                    if (result.Get("source_kept_by_library_setting") is PyBool { Value: true })
                    {
                        processedReason += " The original download was kept in the watched folder, as this library asks.";
                    }

                    if (await RemuxPassFileState.MarkFileStatusAsync(uow, library.Id, rel, ProcessingFileStatuses.Processed, processedReason, now).ConfigureAwait(false))
                    {
                        await RemuxPassFileState.ClearFailureFieldsAsync(uow, library.Id, rel).ConfigureAwait(false);
                    }

                    await RemuxPassFileState.RecordProcessedSourceAsync(
                        uow,
                        library.Id,
                        rel,
                        result.Get("source_fingerprint_size") is PyInt size ? (long)size.Value : null,
                        result.Get("source_fingerprint_mtime_ns") is PyInt mtime ? (long)mtime.Value : null).ConfigureAwait(false);

                    // #652: exactly which copy this pass handed back, so it can be released safely once a manager has it.
                    // Only a copy this pass wrote itself: after a collision skip the file at that path is not Weir's.
                    if (result.Get("output_file") is PyStr { Value.Length: > 0 } handedBack &&
                        result.Get("output_collision_action") is PyStr { Value: "write" })
                    {
                        await HandbackStore.RecordWrittenAsync(uow, library.Id, rel, handedBack.Value, now).ConfigureAwait(false);
                    }
                },
                _logger,
                "file outcome").ConfigureAwait(false);
            foreach (var (key, value) in updates.Items)
            {
                result.Set(key, value);
            }
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            // The media pass remains the source of truth; a false failure over a completed file would be worse.
            _logger.LogWarning(exception, "Weir could not save the durable Files state; inspect the processing record.");
        }
    }

    /// <summary>Records a preflight failure through the same Files/Activity contract as a run result.</summary>
    private async Task RecordFailedResultAsync(PyDict payload, long? libraryId, string mediaScope, PyDict? origin)
    {
        await ApplyFileOutcomeStateAsync(payload, libraryId, mediaScope, origin).ConfigureAwait(false);
        await RecordAsync(payload).ConfigureAwait(false);
    }

    /// <summary><c>str(value or fallback)</c>.</summary>
    private static string TextOr(PyJson? value, string fallback) => value is { IsTruthy: true } ? PyConvert.Str(value) : fallback;

    /// <summary><c>" ".join(text.split())</c>.</summary>
    private static string CollapseWhitespace(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
