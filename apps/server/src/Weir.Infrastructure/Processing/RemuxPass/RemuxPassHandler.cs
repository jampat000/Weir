using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Weir.Core.Activity;
using Weir.Core.Configuration;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.Media;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Core.Processing.RemuxPass;
using Weir.Core.Rules;
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
public sealed class RemuxPassHandler : IJobHandler
{
    private readonly SqliteDatabase _database;
    private readonly WeirOptions _options;
    private readonly RemuxPassRunner _runner;
    private readonly IFailurePolicy _failurePolicy;
    private readonly HandoffCompletionReporter? _reporter;
    private readonly ProcessingJobStore? _jobs;
    private readonly TimeProvider _time;
    private readonly ILogger<RemuxPassHandler> _logger;

    public RemuxPassHandler(
        SqliteDatabase database,
        WeirOptions options,
        RemuxPassRunner runner,
        IFailurePolicy failurePolicy,
        TimeProvider time,
        ILogger<RemuxPassHandler> logger,
        HandoffCompletionReporter? reporter = null,
        ProcessingJobStore? jobs = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _failurePolicy = failurePolicy ?? throw new ArgumentNullException(nameof(failurePolicy));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reporter = reporter;
        _jobs = jobs;
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

        var progress = new ActivityProgressReporter(_database, context.Id, provenance, _logger, _time);
        var result = await _runner.RunAsync(
            new RemuxPassRequest
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
            },
            cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// A file Weir could not read from start to finish is looked at again after a while, and only a few times (#646).
    /// </summary>
    /// <remarks>
    /// Reading a file to the end is how Weir tells a download that is still arriving from one that is damaged, and it cannot
    /// tell them apart from one look. So the look is repeated, further apart each time, while the file is still changing or
    /// might be, and not for ever: otherwise every scan would reread the whole file, gigabytes of disk reads every few
    /// minutes for a file nobody is ever told about. Once the file has sat unchanged through <see cref="UnreadableWaitMinutes"/>, it is
    /// damaged, so this hands it to the library's failure policy with the evidence a reject needs (#471) — the same place a
    /// file whose contents cannot be read at all ends up. A file that changes starts the count again.
    /// </remarks>
    private async Task SettleUnreadableSourceAsync(long jobId, PyDict data, PyDict? origin, PyDict result, CancellationToken cancellationToken)
    {
        if (result.Get("not_ready_kind") is not PyStr { Value: RemuxPassRunner.UnreadableWait })
        {
            return;
        }

        var fingerprint = SourceFingerprint(result.Get("inspected_source_path") as PyStr);
        var last = data.Get("unreadable_looks") is PyInt counted ? (long)counted.Value : 0;
        // A look Weir could not measure the file for decides nothing: it neither counts against the file nor clears its
        // record. Only a look that found the same size and time as the one before is another look at the same file.
        var looks = fingerprint is null
            ? Math.Max(1, last)
            : data.Get("unreadable_fingerprint") is PyStr previous && previous.Value == fingerprint
                ? last + 1
                : 1;
        if (_jobs is null)
        {
            // Nothing can queue the next look, so the file keeps waiting as it did before, rather than be called damaged
            // on the strength of one read.
            return;
        }

        var waited = UnreadableWaitMinutes.Take((int)Math.Min(looks - 1, UnreadableWaitMinutes.Count)).Sum();
        if (looks > UnreadableWaitMinutes.Count)
        {
            var reason = result.Get("reason") is PyStr said ? said.Value : "Weir could not read this file from start to finish.";
            var sentence =
                $"Weir looked at this file {looks.ToString(CultureInfo.InvariantCulture)} times over about " +
                $"{waited.ToString(CultureInfo.InvariantCulture)} minutes and could not read it from start to finish, and it has not " +
                $"changed since the first look. It is damaged or incomplete rather than still arriving. {reason}";
            result.Remove("retryable_wait");
            result.Remove("not_ready_kind");
            result.Set("outcome", RemuxPassOutcomes.FailedBeforeExecution)
                .Set("preflight_status", "failed")
                .Set("preflight_reason", sentence)
                .Set("reason", sentence)
                // Evidence the release is bad, so a library set to reject can act on it (#471).
                .Set("content_unusable", true);
            _logger.LogWarning("A file did not read to the end after {Looks} looks, so Weir stopped waiting for it: job {JobId}.", looks, jobId);
            return;
        }

        var lookAgainAt = _time.GetUtcNow().AddMinutes(UnreadableWaitMinutes[(int)looks - 1]);
        var payload = data.Copy().Set("unreadable_looks", looks);
        if (fingerprint is not null)
        {
            payload.Set("unreadable_fingerprint", fingerprint);
        }

        if (origin is { IsTruthy: true })
        {
            payload.Set("origin", origin);
        }

        try
        {
            await _jobs.EnqueueOrGetAsync(
                $"{RemuxPassOutcomes.JobKind}:unreadable-wait:{jobId}:{looks.ToString(CultureInfo.InvariantCulture)}",
                RemuxPassOutcomes.JobKind,
                PyJsonWriter.Dumps(payload, PyJsonFormat.Compact),
                cancellationToken: cancellationToken,
                notBefore: lookAgainAt).ConfigureAwait(false);
            result.Set("retry_scheduled", true).Set("failure_next_retry_at", PyDateTime.FromDateTimeOffset(lookAgainAt).IsoFormat());
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            // The file stays on hold with its reason, which is true; it is only the next look that is missing.
            _logger.LogWarning(exception, "Weir could not queue another look at a file that would not read to the end.");
        }
    }

    /// <summary>The size and modification time of the file this look read, as one string, or null when it cannot be read.</summary>
    private static string? SourceFingerprint(PyStr? inspectedSourcePath)
    {
        if (inspectedSourcePath is not { Value.Length: > 0 } path)
        {
            return null;
        }

        try
        {
            var info = new FileInfo(path.Value);
            return info.Exists
                ? $"{info.Length.ToString(CultureInfo.InvariantCulture)}:{info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)}"
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// A file that is only too young is looked at again once it is old enough (#632).
    /// </summary>
    /// <remarks>
    /// The folder scan has always done this for itself: a young file is on hold until the next scan. A hand-off has no
    /// next scan - a library fed only by hand-offs may have scanning switched off - so the second look is a job, held back
    /// with <c>not_before</c> exactly as a retry's backoff is. It carries the hand-off's origin, so the outcome is still
    /// reported to the media manager that sent it; marking this result <c>retry_scheduled</c> is what stops the reporter
    /// telling that manager anything yet, because nothing is final. Nothing here is specific to any one manager.
    /// </remarks>
    private async Task DeferUntilOldEnoughAsync(long jobId, PyDict data, PyDict? origin, PyDict result, CancellationToken cancellationToken)
    {
        if (_jobs is null || result.Get("not_ready_kind") is not PyStr { Value: RemuxPassRunner.MinimumAgeWait })
        {
            return;
        }

        var waits = data.Get("minimum_age_waits") is PyInt counted ? (long)counted.Value : 0;
        if (waits >= MaxMinimumAgeWaits)
        {
            var stopped = "This file has kept changing, so Weir has stopped looking at it. When the copy has finished, use Check again from Files.";
            result.Set("reason", stopped).Set("preflight_reason", stopped).Set("failure_operator_message", stopped);
            _logger.LogWarning("A file was still changing after {Waits} looks, so Weir stopped looking: job {JobId}.", waits, jobId);
            return;
        }

        var seconds = result.Get("not_ready_seconds") is PyInt remaining ? Math.Max(1, (long)remaining.Value) : 60;
        // A little past the moment it is old enough, so the second look does not land a fraction of a second early.
        var lookAgainAt = _time.GetUtcNow().AddSeconds(seconds + 2);
        var payload = data.Copy().Set("minimum_age_waits", waits + 1);
        if (origin is { IsTruthy: true })
        {
            payload.Set("origin", origin);
        }

        try
        {
            await _jobs.EnqueueOrGetAsync(
                $"{RemuxPassOutcomes.JobKind}:minimum-age-wait:{jobId}:{waits + 1}",
                RemuxPassOutcomes.JobKind,
                PyJsonWriter.Dumps(payload, PyJsonFormat.Compact),
                cancellationToken: cancellationToken,
                notBefore: lookAgainAt).ConfigureAwait(false);
            result.Set("retry_scheduled", true).Set("failure_next_retry_at", PyDateTime.FromDateTimeOffset(lookAgainAt).IsoFormat());
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            // The file stays on hold with its reason, which is true; it is only the second look that is missing.
            _logger.LogWarning(exception, "Weir could not queue a second look at a file that is waiting out its minimum age.");
        }
    }

    private sealed record Claim(
        PyDict? Failure,
        ProcessingOperatorSettingsRecord? Operator = null,
        ProcessingLibraryRecord? Library = null,
        ProcessingRulesConfig? Rules = null,
        ProcessingPathRuntime? Runtime = null);

    /// <summary>
    /// Read what the pass needs and mark the file as being processed, then commit: no ffprobe or ffmpeg work runs while the
    /// worker holds a transaction.
    /// </summary>
    private Task<Claim> ClaimAsync(JobWorkContext context, string rel, string mediaScope, long? libraryId, CancellationToken cancellationToken) =>
        LockedWrites.RunAsync(
            _database,
            async uow =>
            {
                var operatorSettings = await OperatorSettingsStore.EnsureAsync(uow).ConfigureAwait(false);
                var library = await ResolveLibraryAsync(uow, libraryId, mediaScope).ConfigureAwait(false);
                var rules = library is not null ? await RulesConfigForAsync(uow, library).ConfigureAwait(false) : null;
                rules ??= await LoadScopeRulesConfigAsync(uow, mediaScope).ConfigureAwait(false);
                ProcessingPathRuntime? runtime;
                string? problem;
                if (library is null)
                {
                    var label = mediaScope == "tv" ? "TV" : "Movies";
                    (runtime, problem) = (null, $"No library covers {label}. Add one on Processing → Libraries, then queue this work again.");
                }
                else
                {
                    (runtime, problem) = RemuxPassPaths.RuntimeForLibrary(library, _options.WeirHome);
                }

                if (problem is not null)
                {
                    return new Claim(new PyDict()
                        .Set("job_id", context.Id)
                        .Set("ok", false)
                        .Set("outcome", RemuxPassOutcomes.FailedBeforeExecution)
                        .Set("reason", problem)
                        .Set("relative_media_path", rel)
                        .Set("library_id", library?.Id ?? libraryId));
                }

                if (library is not null)
                {
                    await RemuxPassFileState.MarkFileStatusAsync(uow, library.Id, rel, ProcessingFileStatuses.Processing, "Weir has claimed this file and is checking it now.", _time.GetUtcNow())
                        .ConfigureAwait(false);
                }

                return new Claim(null, operatorSettings, library, rules, runtime);
            },
            _logger,
            "claim processing file",
            cancellationToken);

    /// <summary>The pass's library: by id when the payload carries one, else the seeded library for its scope.</summary>
    public static async Task<ProcessingLibraryRecord?> ResolveLibraryAsync(UnitOfWork uow, long? libraryId, string? mediaScope)
    {
        if (libraryId is { } id && await LibraryStore.GetAsync(uow, id).ConfigureAwait(false) is { } found)
        {
            return found;
        }

        return await LibraryStore.SeededForScopeAsync(uow, mediaScope ?? "movie").ConfigureAwait(false);
    }

    private static async Task<ProcessingRulesConfig?> RulesConfigForAsync(UnitOfWork uow, ProcessingLibraryRecord library) =>
        library.RuleSetId is { } ruleSetId && await LibraryStore.GetRuleSetAsync(uow, ruleSetId).ConfigureAwait(false) is { } ruleSet
            ? RemuxPassPaths.RulesConfigFor(ruleSet)
            : null;

    /// <summary>The seeded library's rule set for the scope, or the shipped defaults.</summary>
    private static async Task<ProcessingRulesConfig> LoadScopeRulesConfigAsync(UnitOfWork uow, string mediaScope)
    {
        var seeded = await LibraryStore.SeededForScopeAsync(uow, mediaScope).ConfigureAwait(false);
        var ruleSet = seeded?.RuleSetId is { } id ? await LibraryStore.GetRuleSetAsync(uow, id).ConfigureAwait(false) : null;
        return RuleSetConversion.ToRulesConfig(ruleSet);
    }

    private async Task<PyDict?> CarriedOriginAsync(long jobId, long? libraryId, string rel, string mediaScope)
    {
        try
        {
            var uow = await UnitOfWork.OpenAsync(_database).ConfigureAwait(false);
            await using (uow.ConfigureAwait(false))
            {
                var library = await ResolveLibraryAsync(uow, libraryId, mediaScope).ConfigureAwait(false);
                return await HandoffOriginCarry.FindAsync(uow, library?.Id ?? libraryId, rel, jobId).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            _logger.LogWarning(exception, "Weir could not look up the hand-off this file came from.");
            return null;
        }
    }

    /// <summary>The origin written onto this job's own row after it started, or null.</summary>
    private async Task<PyDict?> AdoptedOriginAsync(long jobId)
    {
        try
        {
            var uow = await UnitOfWork.OpenAsync(_database).ConfigureAwait(false);
            await using (uow.ConfigureAwait(false))
            {
                var payload = await uow.ScalarAsync("SELECT payload_json FROM jobs WHERE id = @id", ("@id", jobId)).ConfigureAwait(false);
                return payload is string text && PyJsonParser.Parse(text) is PyDict dict ? dict.Get("origin") as PyDict : null;
            }
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException or PyJsonDecodeException)
        {
            _logger.LogWarning(exception, "Weir could not check whether a hand-off took over this pass.");
            return null;
        }
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

/// <summary>
/// <c>ProcessingActivityProgressReporter</c>: one live <c>processing.file_processing_progress</c> row per pass, inserted on the first
/// update and rewritten after. A failed write never interrupts ffprobe or ffmpeg.
/// </summary>
public sealed class ActivityProgressReporter
{
    private readonly SqliteDatabase _database;
    private readonly long _jobId;
    private readonly PyDict _extra;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();

    public ActivityProgressReporter(SqliteDatabase database, long jobId, PyDict extra, ILogger logger, TimeProvider time)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _jobId = jobId;
        _extra = extra ?? new PyDict();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>The progress row, once written; the handler turns it into the completed row.</summary>
    public long? ActivityId { get; private set; }

    public void Report(PyDict payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var body = new PyDict().Set("job_id", _jobId);
        foreach (var (key, value) in _extra.Items.Concat(payload.Items))
        {
            body.Set(key, value);
        }

        // The row is rewritten in place, so its created_at stays at the pass's start. This is how a reader
        // tells a long pass that is still reporting from one that died (LiveProgressStore).
        body.Set("reported_at", PyDateTime.UtcNow(_time).PydanticJson());

        lock (_lock)
        {
            var delay = TimeSpan.FromSeconds(0.1);
            for (var attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    Write(body);
                    return;
                }
                catch (SqliteException exception) when (LockedWrites.IsLock(exception) && attempt < 3)
                {
                    Thread.Sleep(delay);
                    delay *= 2;
                }
#pragma warning disable CA1031 // A progress update must never interrupt the media pass.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    _logger.LogWarning(exception, "Weir could not save a progress update; continuing the media pass.");
                    return;
                }
            }
        }
    }

    private void Write(PyDict body)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        var name = FileName(body.Get("relative_media_path"));
        var detail = PyStrings.Slice(PyJsonWriter.Dumps(body, PyJsonFormat.Compact), 6000);
        if (ActivityId is null)
        {
            var id = SqliteActivityWriter.Record(connection, transaction, new ActivityEventDraft(ActivityEventTypes.ProcessingFileProcessingProgress, "processing", $"Processing {name}", detail));
            transaction.Commit();
            ActivityNotifications.TransactionCommitted(_database, transaction);
            ActivityId = id;
            return;
        }

        var title = (body.Get("status") is { IsTruthy: true } status ? PyConvert.Str(status) : "processing") switch
        {
            "waiting" => $"Waiting to process {name}",
            "finishing" => $"Finishing {name}",
            "finished" => $"{name} finished processing",
            "failed" => $"{name} could not be processed",
            _ => $"Processing {name}",
        };
        SqliteActivityWriter.Update(connection, transaction, ActivityId.Value, title: title, detail: detail);
        transaction.Commit();
        ActivityNotifications.TransactionCommitted(_database, transaction);
    }

    /// <summary><c>Path(str(relative_media_path or "")).name or "this file"</c>.</summary>
    private static string FileName(PyJson? value)
    {
        var text = value is { IsTruthy: true } ? PyConvert.Str(value) : string.Empty;
        var name = MediaPathNames.Name(text, OperatingSystem.IsWindows());
        return name.Length > 0 ? name : "this file";
    }
}
