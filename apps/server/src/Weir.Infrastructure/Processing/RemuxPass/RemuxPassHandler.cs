using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Weir.Core.Activity;
using Weir.Core.Configuration;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Core.Processing.RemuxPass;
using Weir.Core.Time;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>
/// The worker handler for <c>processing.file.remux_pass.v1</c>: claim the file, run
/// the pass outside any transaction, keep the Files row and Activity in step with the result, apply the library's failure
/// policy, and report back to the manager that handed the file over.
/// </summary>
/// <remarks>
/// #531 item 2: when a retried or requeued payload has lost its hand-off origin, the origin of the most recent job for the
/// same file is carried forward (<see cref="HandoffOriginCarry"/>), so the final outcome is still reported with its output path.
/// </remarks>
public sealed partial class RemuxPassHandler : IJobHandler
{
    private readonly SqliteDatabase _database;
    private readonly WeirOptions _options;
    private readonly RemuxPassRunner _runner;
    private readonly IFailurePolicy _failurePolicy;
    private readonly HandoffCompletionReporter? _reporter;
    private readonly ProcessingJobStore? _jobs;
    private readonly TimeProvider _time;
    private readonly ILogger<RemuxPassHandler> _logger;
    private readonly LiveProgressStore _liveProgress;

    public RemuxPassHandler(
        SqliteDatabase database,
        WeirOptions options,
        RemuxPassRunner runner,
        IFailurePolicy failurePolicy,
        TimeProvider time,
        ILogger<RemuxPassHandler> logger,
        HandoffCompletionReporter? reporter = null,
        ProcessingJobStore? jobs = null,
        LiveProgressStore? liveProgress = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _failurePolicy = failurePolicy ?? throw new ArgumentNullException(nameof(failurePolicy));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reporter = reporter;
        _jobs = jobs;
        _liveProgress = liveProgress ?? new LiveProgressStore();
    }

    /// <summary>
    /// How many times a file that is only waiting out the minimum file age is looked at again before Weir stops
    /// looking (#632). Each look is a minute or so apart, so this is about half an hour of a file that never stops
    /// changing, which is a copy that has gone wrong rather than one that is finishing.
    /// </summary>
    public const int MaxMinimumAgeWaits = 30;

    /// <summary>
    /// How long Weir waits between looks at a file it could not read from start to finish, and so how long it treats one as
    /// still arriving (#646). After the last of these, a file that has not changed at all is damaged rather than unfinished,
    /// and the library's failure policy decides what happens to it. About an hour of patience in all.
    /// </summary>
    public static readonly IReadOnlyList<int> UnreadableWaitMinutes = [5, 15, 45];

    public string JobKind => RemuxPassOutcomes.JobKind;

    public async Task HandleAsync(JobWorkContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var raw = PyStrings.Strip(context.PayloadJson ?? string.Empty);
        if (raw.Length == 0)
        {
            await RecordAsync(FailedPayload(context.Id, "missing payload_json")).ConfigureAwait(false);
            return;
        }

        PyJson parsed;
        try
        {
            parsed = PyJsonParser.Parse(raw);
        }
        catch (PyJsonDecodeException exception)
        {
            await RecordAsync(FailedPayload(context.Id, $"invalid json: {exception.Message}")).ConfigureAwait(false);
            return;
        }

        if (parsed is not PyDict data)
        {
            await RecordAsync(FailedPayload(context.Id, "payload must be a JSON object")).ConfigureAwait(false);
            return;
        }

        var provenance = ActivityProvenance.JobProvenance(data);
        if (data.Get("relative_media_path") is not PyStr relValue || PyStrings.Strip(relValue.Value).Length == 0)
        {
            await RecordAsync(Merge(FailedPayload(context.Id, "relative_media_path is required"), provenance)).ConfigureAwait(false);
            return;
        }

        var rel = PyStrings.Strip(relValue.Value);
        if (data.Get("dry_run") is { } dryRun && dryRun is not PyNull)
        {
            var legacy = FailedPayload(
                    context.Id,
                    "This job payload uses legacy Weir dry_run, which is no longer supported. Re-enqueue without dry_run.")
                .Set("relative_media_path", rel);
            await RecordFailedResultAsync(Merge(legacy, provenance), null, "movie", null).ConfigureAwait(false);
            return;
        }

        var mediaScope = data.Get("media_scope") is PyStr { Value: "movie" or "tv" } scopeValue ? scopeValue.Value : "movie";
        long? libraryId = data.Get("library_id") is PyInt libraryValue ? (long)libraryValue.Value : null;
        var passThrough = data.Get("pass_through_unchanged") is PyBool { Value: true };
        var manualPlan = ManualPlanJson.FromPyJson(data.Get("manual_plan"));
        var manualPlanFingerprint = ManualPlanJson.FingerprintFromPyJson(data.Get("source_fingerprint"));

        var origin = data.Get("origin") as PyDict;
        var payloadJson = context.PayloadJson;
        if (origin is null)
        {
            origin = await CarriedOriginAsync(context.Id, libraryId, rel, mediaScope).ConfigureAwait(false);
            if (origin is not null)
            {
                var carried = data.Copy().Set("origin", origin);
                payloadJson = PyJsonWriter.Dumps(carried, PyJsonFormat.Compact);
            }
        }

        var claim = await ClaimAsync(context, rel, mediaScope, libraryId, cancellationToken).ConfigureAwait(false);
        if (claim.Failure is { } failure)
        {
            Merge(failure, provenance);
            await RecordFailedResultAsync(failure, libraryId, mediaScope, origin).ConfigureAwait(false);
            await ReportBackAsync(payloadJson, failure).ConfigureAwait(false);
            return;
        }

        var progress = new ActivityProgressReporter(_database, context.Id, provenance, _logger, _time, _liveProgress);
        var request = new RemuxPassRequest
        {
            Runtime = claim.Runtime!,
            RelativeMediaPath = rel,
            LibraryId = claim.Library?.Id ?? libraryId,
            RulesConfig = claim.Rules,
            MinFileAgeSeconds = claim.Operator!.MinFileAgeSeconds,
            MinInputFileSizeMb = Math.Max(claim.Operator.ProcessingMinInputFileSizeMb, claim.Library?.MinFileSizeMb ?? 0),
            MinimumFreeDiskSpaceMb = claim.Operator.MinimumFreeDiskSpaceMb,
            KeepFailedWorkFiles = claim.Operator.KeepFailedWorkFiles,
            MediaScope = mediaScope,
            CurrentJobId = context.Id,
            ProgressReporter = progress.Report,
            PassThroughUnchanged = passThrough,
            Origin = HandoffOrigin.FromPayload(origin is null ? null : new PyDict().Set("origin", origin)),
            ManualPlan = manualPlan,
            ManualPlanFingerprint = manualPlanFingerprint,
        };
        PyDict result;
        try
        {
            result = await _runner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Before RecordAsync turns the progress row into the completed row, so no late progress save overwrites it.
            await progress.CompleteAsync().ConfigureAwait(false);
        }

        // A hand-off that arrived while this pass was running took the pass over (MediaManagerIntake.AdoptActivePass)
        // and wrote its origin onto this job's row; pick it up now so the outcome is recorded and called back for it.
        if (origin is null && await AdoptedOriginAsync(context.Id).ConfigureAwait(false) is { } adopted)
        {
            origin = adopted;
            payloadJson = PyJsonWriter.Dumps(data.Copy().Set("origin", adopted), PyJsonFormat.Compact);
        }

        result.Set("job_id", context.Id);
        result.Set("library_id", claim.Library?.Id ?? libraryId);
        if (result.Get("rejection_kind") is { IsTruthy: true } && claim.Library is { } library)
        {
            var action = string.Equals(PyStrings.Strip(library.RejectedFileAction ?? string.Empty), "delete_file", StringComparison.OrdinalIgnoreCase)
                         // Under reject, the reject job removes the download, and only after the manager accepts.
                         && ProcessingFailurePolicies.Normalize(library.FailurePolicy) != ProcessingFailurePolicies.Reject
                ? "delete_file"
                : "leave";
            result.Set("rejected_file_action", action);
            if (action == "delete_file")
            {
                result.Set("rejected_cleanup_status", "pending");
                result.Set("rejected_cleanup_detail", "This library is set to delete rejected files. Weir recorded the rejection and will now remove only this file.");
            }
            else
            {
                result.Set("rejected_cleanup_status", "left_in_place");
                result.Set("rejected_cleanup_detail", "Weir left the rejected file in place because this library's cleanup action is Leave in place.");
            }
        }

        await SettleUnreadableSourceAsync(context.Id, data, origin, result, cancellationToken).ConfigureAwait(false);
        Merge(result, provenance);
        await ApplyFileOutcomeStateAsync(result, libraryId, mediaScope, origin).ConfigureAwait(false);
        await DeferUntilOldEnoughAsync(context.Id, data, origin, result, cancellationToken).ConfigureAwait(false);
        await RecordAsync(result, progress.ActivityId).ConfigureAwait(false);
        await FinishRejectedInputCleanupAsync(result, libraryId, mediaScope).ConfigureAwait(false);
        await ReportBackAsync(payloadJson, result).ConfigureAwait(false);
    }

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

    /// <summary>
    /// Writes the processing record and the Activity row together so they cannot disagree. A progress row
    /// already started for the pass becomes the completed row.
    /// </summary>
    internal async Task RecordAsync(PyDict payload, long? activityId = null)
    {
        var detail = RemuxPassVisibility.ActivityDetail(payload);
        var title = RemuxPassVisibility.ActivityTitle(payload);
        try
        {
            await LockedWrites.RunAsync(
                _database,
                async uow =>
                {
                    if (payload.Get("relative_media_path") is PyStr rel && PyStrings.Strip(rel.Value).Length > 0)
                    {
                        try
                        {
                            await RemuxPassFileState.RecordFileLogAsync(uow, rel.Value, title, payload, _time.GetUtcNow()).ConfigureAwait(false);
                        }
                        catch (SqliteException exception) when (LockedWrites.IsLock(exception))
                        {
                            throw;
                        }
                        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
                        {
                            _logger.LogError(exception, "Weir could not write the processing record for {Path}.", rel.Value);
                        }
                    }

                    if (activityId is { } id &&
                        await SqliteActivityWriter.UpdateAsync(uow, id, ActivityEventTypes.ProcessingFileRemuxPassCompleted, title, detail).ConfigureAwait(false))
                    {
                        return;
                    }

                    await SqliteActivityWriter.RecordAsync(uow, new ActivityEventDraft(ActivityEventTypes.ProcessingFileRemuxPassCompleted, "processing", title, detail))
                        .ConfigureAwait(false);
                },
                _logger,
                "processing record").ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            // Activity is observability, never a prerequisite for a safe media mutation.
            _logger.LogWarning(exception, "Weir could not save the processing record; the pass result remains authoritative.");
        }
    }

    /// <summary>Applies an opt-in rejection deletion, after the rejection was recorded.</summary>
    private async Task FinishRejectedInputCleanupAsync(PyDict result, long? libraryId, string mediaScope)
    {
        if (result.Get("rejection_kind") is not { IsTruthy: true } || result.Get("rejected_file_action") is not PyStr { Value: "delete_file" })
        {
            return;
        }

        if (result.Get("processing_watched_folder_resolved") is not PyStr watched || result.Get("inspected_source_path") is not PyStr inspected)
        {
            result.Set("rejected_cleanup_status", "not_deleted");
            result.Set("rejected_cleanup_detail", "Weir did not delete the rejected file because its verified watched-folder path was unavailable.");
        }
        else
        {
            var cleanup = RemuxPassPaths.CleanupRejectedFile(watched.Value, inspected.Value, "delete_file");
            result.Set("rejected_cleanup_status", cleanup.Deleted ? "deleted" : "not_deleted");
            result.Set("rejected_cleanup_detail", cleanup.Detail);
        }

        await ApplyFileOutcomeStateAsync(result, libraryId, mediaScope, null).ConfigureAwait(false);
        if (result.Get("relative_media_path") is not PyStr rel || PyStrings.Strip(rel.Value).Length == 0)
        {
            return;
        }

        await LockedWrites.RunAsync(
            _database,
            uow => RemuxPassFileState.RecordFileLogAsync(uow, rel.Value, "Rejected file cleanup finished", result, _time.GetUtcNow()),
            _logger,
            "rejected file cleanup record").ConfigureAwait(false);
    }

    /// <summary>Tells the originating manager how the pass went. Best effort; never throws.</summary>
    private async Task ReportBackAsync(string? payloadJson, PyDict result)
    {
        if (_reporter is null)
        {
            return;
        }

        try
        {
            var uow = await UnitOfWork.OpenAsync(_database).ConfigureAwait(false);
            await using (uow.ConfigureAwait(false))
            {
                var status = await _reporter.ReportHandoffCompletionAsync(uow, payloadJson, result).ConfigureAwait(false);
                if (!status.StartsWith("skipped", StringComparison.Ordinal))
                {
                    _logger.LogInformation("Hand-off callback: {Status}", status);
                }
            }
        }
#pragma warning disable CA1031 // Reporting must never break the job.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogError(exception, "Hand-off callback raised unexpectedly.");
        }
    }

    private static PyDict FailedPayload(long jobId, string reason) => new PyDict()
        .Set("job_id", jobId)
        .Set("ok", false)
        .Set("outcome", RemuxPassOutcomes.FailedBeforeExecution)
        .Set("reason", reason);

    /// <summary><c>d.update(other)</c>.</summary>
    private static PyDict Merge(PyDict target, PyDict other)
    {
        foreach (var (key, value) in other.Items)
        {
            target.Set(key, value);
        }

        return target;
    }

    /// <summary><c>str(value or fallback)</c>.</summary>
    private static string TextOr(PyJson? value, string fallback) => value is { IsTruthy: true } ? PyConvert.Str(value) : fallback;

    /// <summary><c>" ".join(text.split())</c>.</summary>
    private static string CollapseWhitespace(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
