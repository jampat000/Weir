using Microsoft.Extensions.Logging;
using Weir.Core.Activity;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.Observability;
using Weir.Core.Time;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// One worker pass: claim at most one job under the current admission, run its handler, then complete
/// or fail it.
/// </summary>
public sealed class ProcessingJobProcessor
{
    public const int DefaultLeaseSeconds = 300;
    public const string TerminalizationFailurePrefix = "processing_terminalization_failure: ";
    public const string Module = "Weir";

    private readonly ProcessingJobStore _queue;
    private readonly JobHandlerRegistry _handlers;
    private readonly IActivityWriter _activity;
    private readonly IUnhandledJobFailureRecorder _failureRecorder;
    private readonly IJobNotifications _notifications;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    public ProcessingJobProcessor(
        ProcessingJobStore queue,
        JobHandlerRegistry handlers,
        IActivityWriter activity,
        IUnhandledJobFailureRecorder failureRecorder,
        IJobNotifications notifications,
        TimeProvider time,
        ILogger<ProcessingJobProcessor> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _failureRecorder = failureRecorder ?? throw new ArgumentNullException(nameof(failureRecorder));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Kinds = ClaimableKinds.For(handlers);
    }

    /// <summary>
    /// The kinds this processor claims. Defaults to the registry's kinds plus refused kinds; null claims
    /// every kind (used by tests).
    /// </summary>
    public ClaimableKinds? Kinds { get; init; }

    /// <summary>Test seam replacing <see cref="ProcessingJobStore.CompleteClaimedAsync"/>.</summary>
    internal Func<long, string, DateTimeOffset, Task<bool>>? CompleteOverride { get; init; }

    /// <summary>
    /// Test seam: called each time the lease-renewal heartbeat has started waiting for its next tick, which
    /// is after any previous renewal has been written. A test on a fake clock waits for it between advances
    /// instead of sleeping and hoping the renewal landed and the next timer was registered.
    /// </summary>
    internal Action? LeaseRenewalWaiting { get; init; }

    public async Task<JobProcessOutcome> ProcessOneAsync(
        string leaseOwner,
        int leaseSeconds = DefaultLeaseSeconds,
        DateTimeOffset? now = null,
        WorkLane lane = WorkLane.Files,
        ClaimableKinds? kinds = null,
        CancellationToken cancellationToken = default)
    {
        var when = Timestamp.TruncateToMicroseconds((now ?? _time.GetUtcNow()).ToUniversalTime());
        var leaseUntil = when + TimeSpan.FromSeconds(leaseSeconds);

        // The schedule and the pause are evaluated at lease time, not only at enqueue (#337). A job
        // already leased is left to finish. A caller running the upkeep lane (#717) passes its own kinds and
        // lane, so a scan or sweep is never gated by the files-at-once limit it is exempt from.
        var job = await _queue.ClaimNextAdmittedAsync(leaseOwner, leaseUntil, when, kinds ?? Kinds, lane, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return JobProcessOutcome.Idle;
        }

        var context = new JobWorkContext(
            job.Id,
            job.JobKind,
            job.PayloadJson,
            leaseOwner,
            job.AttemptCount == 0 ? 1 : job.AttemptCount,
            job.MaxAttempts == 0 ? 1 : job.MaxAttempts);
        var willRetry = WorkerFailures.RetryComing(context.AttemptCount, context.MaxAttempts);

        if (JobKindGuard.IsRetired(context.JobKind))
        {
            await FailQuietlyAsync(
                context,
                WorkerFailures.RefusedJobError(Module, WorkerFailures.RetiredKindReason(context.JobKind, context.Id), willRetry),
                when,
                "fail_claimed after retired job_kind guard job_id={JobId}").ConfigureAwait(false);
            return JobProcessOutcome.Processed;
        }

        if (!JobKindGuard.HasProcessingPrefix(context.JobKind))
        {
            await FailQuietlyAsync(
                context,
                WorkerFailures.RefusedJobError(Module, WorkerFailures.UnprefixedKindReason(context.JobKind, context.Id), willRetry),
                when,
                "fail_claimed after processing.* prefix guard job_id={JobId}").ConfigureAwait(false);
            return JobProcessOutcome.Processed;
        }

        var handler = _handlers.Find(context.JobKind);
        if (handler is null)
        {
            await FailQuietlyAsync(
                context,
                WorkerFailures.StoredError(WorkerFailures.JobFailure(Module, WorkerFailures.NoHandler(context.JobKind), willRetry)),
                when,
                "fail_claimed_processing_job failed after missing handler job_id={JobId}").ConfigureAwait(false);
            return JobProcessOutcome.Processed;
        }

        try
        {
            using (_logger.BeginScope(new Dictionary<string, object> { ["job_id"] = context.Id }))
            {
                await RunHandlerWithLeaseRenewalAsync(handler, context, leaseSeconds, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down: the row stays leased and startup recovery requeues it.
            throw;
        }
#pragma warning disable CA1031 // A handler failure of any kind fails the job; the worker must survive it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            var failure = WorkerFailures.JobFailure(Module, FailureMessages.FromDotNet(exception), willRetry);
            _logger.LogError(
                "Job handler failed for job_id={JobId} kind={JobKind}: {Message} {Detail}",
                context.Id,
                context.JobKind,
                failure.Message,
                failure.TechnicalDetail);
            if (exception is not AlreadyRecordedFailureException)
            {
                // A handler that recorded its own failure has said it once already (#488).
                await RecordUnhandledFailureAsync(context, failure.Message).ConfigureAwait(false);
            }

            await FailQuietlyAsync(
                context,
                WorkerFailures.StoredError(failure),
                when,
                "fail_claimed_processing_job failed after handler error job_id={JobId}").ConfigureAwait(false);
            _notifications.Dispatch("processing", "failed", context.Id, context.JobKind, willRetry);
            return JobProcessOutcome.Processed;
        }

        var completeOk = true;
        string? completeError = null;
        try
        {
            var ok = CompleteOverride is not null
                ? await CompleteOverride(context.Id, context.LeaseOwner, when).ConfigureAwait(false)
                : await _queue.CompleteClaimedAsync(context.Id, context.LeaseOwner, when, CancellationToken.None).ConfigureAwait(false);
            if (!ok)
            {
                completeOk = false;
                completeError = "complete_claimed_processing_job refused (lease/state mismatch)";
            }
        }
#pragma warning disable CA1031 // Recording completion failed; the row is terminalised below instead.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            completeOk = false;
            _logger.LogError(exception, "complete_claimed_processing_job failed job_id={JobId}", context.Id);
            completeError = exception.Message;
        }

        if (completeOk)
        {
            _notifications.Dispatch("processing", "completed", context.Id, context.JobKind);
            return JobProcessOutcome.Processed;
        }

        // The handler finished; only recording that failed. Said plainly, with the detail after it.
        var bounded = WireStrings.Slice(
            TerminalizationFailurePrefix + WorkerFailures.StoredError(
                FailureMessages.FromException(
                    Module,
                    "job",
                    FailureMessages.RuntimeError(completeError!),
                    continuation: "The work ran, but Weir could not record that it finished.")),
            JobQueueRules.LastErrorLimit);
        try
        {
            var recovered = await _queue.FailLeasedAfterCompleteFailureAsync(context.Id, context.LeaseOwner, bounded, when, CancellationToken.None)
                .ConfigureAwait(false);
            if (!recovered)
            {
                _logger.LogWarning(
                    "Terminalization recovery did not apply job_id={JobId} owner={Owner}",
                    context.Id,
                    context.LeaseOwner);
            }
        }
#pragma warning disable CA1031 // The last resort after a failed completion is logged; the worker carries on and the lease expires.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogError(exception, "fail_leased_processing_job_after_complete_failure failed job_id={JobId}", context.Id);
        }

        return JobProcessOutcome.Processed;
    }

    /// <summary>
    /// Run the handler while a background heartbeat renews its lease roughly every
    /// <paramref name="leaseSeconds"/> / 3 seconds (#540 item 1), so a handler that runs longer than one
    /// lease (a remux longer than 300 s, say) keeps its row leased instead of letting a second worker claim
    /// it and start a second ffmpeg process on the same file. Completion and failure verify the lease owner
    /// themselves (<see cref="ProcessingJobStore.CompleteClaimedAsync"/> and
    /// <see cref="ProcessingJobStore.FailClaimedAsync"/>); this only keeps the lease alive while the
    /// handler is still running.
    /// </summary>
    private async Task RunHandlerWithLeaseRenewalAsync(IJobHandler handler, JobWorkContext context, int leaseSeconds, CancellationToken cancellationToken)
    {
        using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = RenewLeaseHeartbeatAsync(context.Id, context.LeaseOwner, leaseSeconds, renewalCts.Token);
        try
        {
            await handler.HandleAsync(context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await renewalCts.CancelAsync().ConfigureAwait(false);
            await heartbeat.ConfigureAwait(false);
        }
    }

    /// <summary>The lease-renewal heartbeat itself: sleep, renew, repeat, until cancelled.</summary>
    private async Task RenewLeaseHeartbeatAsync(long jobId, string leaseOwner, int leaseSeconds, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(0.05, leaseSeconds / 3.0));
        try
        {
            while (true)
            {
                var tick = Task.Delay(interval, _time, cancellationToken);
                LeaseRenewalWaiting?.Invoke();
                await tick.ConfigureAwait(false);
                var now = _time.GetUtcNow();
                var renewed = await _queue.RenewLeaseAsync(jobId, leaseOwner, now + TimeSpan.FromSeconds(leaseSeconds), now, CancellationToken.None).ConfigureAwait(false);
                if (!renewed)
                {
                    _logger.LogWarning(
                        "Lease renewal found the lease no longer held job_id={JobId} owner={Owner}; no longer renewing.",
                        jobId,
                        leaseOwner);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The handler finished, or the worker is shutting down: stop renewing either way.
        }
#pragma warning disable CA1031 // A renewal-loop crash must never take the handler down with it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogError(exception, "Lease renewal loop crashed job_id={JobId} owner={Owner}", jobId, leaseOwner);
        }
    }

    /// <summary>
    /// Record an unhandled failure: the file-state policy (#522) and one <c>processing.worker_failure</c>
    /// Activity entry.
    /// </summary>
    private async Task RecordUnhandledFailureAsync(JobWorkContext context, string message)
    {
        var payload = JobPayload.ParseObject(context.PayloadJson);
        var relative = JobPayload.StringProperty(payload, "relative_media_path");
        var relativeTrimmed = relative is not null && relative.Trim().Length > 0 ? relative.Trim() : null;
        var scope = JobPayload.StringProperty(payload, "media_scope") is "movie" or "tv" ? JobPayload.StringProperty(payload, "media_scope")! : "movie";
        var libraryIdValue = payload is { } element && element.TryGetProperty("library_id", out var raw) &&
                             (raw.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False ||
                              JobPayload.LooseInteger(payload, "library_id") is not null)
            ? raw
            : (System.Text.Json.JsonElement?)null;
        var safeMessage = WireStrings.Slice(string.Join(' ', message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), 1200);

        try
        {
            var willRetry = await _failureRecorder.RecordAsync(
                new UnhandledJobFailure(context, JobPayload.LooseInteger(payload, "library_id"), scope, relativeTrimmed, safeMessage),
                CancellationToken.None).ConfigureAwait(false);

            var detail = new WireObject()
                .Set("job_id", context.Id)
                .Set("job_kind", context.JobKind)
                .Set("failure_class", "unknown")
                .Set("message", safeMessage)
                .Set("next_action", "Review this job and use Start again after fixing the cause.")
                .Set("retry_scheduled", willRetry == true)
                .Set("result", willRetry == true ? "retrying" : "failed");
            if (relativeTrimmed is not null)
            {
                detail.Set("relative_media_path", relativeTrimmed);
            }

            if (libraryIdValue is { } libraryId)
            {
                detail.Set("library_id", WireJsonParser.Parse(libraryId.GetRawText()));
            }

            AddProvenance(detail, payload);
            await _activity.RecordAsync(
                new ActivityEventDraft(ActivityEventTypes.ProcessingWorkerFailure, "processing", "A Weir job stopped with an error", WireJsonWriter.Dumps(detail, WireJsonFormat.Compact)),
                CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Diagnostics must never stop the worker from failing the job.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogError(exception, "Failure diagnostics could not be persisted job_id={JobId}", context.Id);
        }
    }

    /// <summary>Copy <c>trigger</c> and <c>run_id</c> from the payload, only when present and valid.</summary>
    internal static void AddProvenance(WireObject detail, System.Text.Json.JsonElement? payload)
    {
        if (payload is not { } element)
        {
            return;
        }

        var trigger = JobPayload.StringProperty(payload, "trigger");
        if (trigger is not null && ActivityClassifier.Triggers.Contains(trigger.Trim().ToLowerInvariant()))
        {
            detail.Set("trigger", trigger.Trim().ToLowerInvariant());
        }

        if (element.TryGetProperty("run_id", out var runId))
        {
            var valid = runId.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => runId.GetString()!.Trim().Length > 0,
                System.Text.Json.JsonValueKind.Number => JobPayload.StrictInteger(payload, "run_id") is not null,
                _ => false,
            };
            if (valid)
            {
                detail.Set("run_id", WireJsonParser.Parse(runId.GetRawText()));
            }
        }
    }

    private async Task FailQuietlyAsync(JobWorkContext context, string errorText, DateTimeOffset when, string logMessage)
    {
        try
        {
            await _queue.FailClaimedAsync(context.Id, context.LeaseOwner, errorText, when, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Log and carry on: the worker must survive a failed write.
        catch (Exception exception)
#pragma warning restore CA1031
        {
#pragma warning disable CA2254 // The message templates are constants chosen by the caller.
            _logger.LogError(exception, logMessage, context.Id);
#pragma warning restore CA2254
        }
    }
}
