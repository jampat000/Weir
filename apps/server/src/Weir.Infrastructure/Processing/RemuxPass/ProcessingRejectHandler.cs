using Microsoft.Extensions.Logging;
using Weir.Core.Activity;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.Media;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>
/// Worker handler for <c>processing.file.reject.v1</c>: tell the media
/// manager a release Weir could not process is bad, so it can find a different one (#465, #471).
/// </summary>
/// <remarks>
/// <para>
/// <b>Report first, remove second.</b> Nothing is removed until the manager has accepted the report (a 2xx answer); "could
/// not reach it" and "it refused" are treated the same: not accepted.
/// </para>
/// <para>
/// <b>Anything short of certainty falls back to pass-through</b> — never to deleting a file nobody was told about, and
/// never to silently doing nothing. The fallback is recorded in Activity with the reason.
/// </para>
/// <para>
/// Fix #532: whichever way the attempt ends, a Files row is upserted (not merely updated) so a rejection that no scan
/// had seen still appears on the Files screen.
/// </para>
/// </remarks>
public sealed partial class ProcessingRejectHandler : IJobHandler
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly SqliteDatabase _database;
    private readonly IMediaManagerPorts _ports;
    private readonly MediaManagerConnectionService _connections;
    private readonly HandoffCompletionReporter _reporter;
    private readonly HandoffLedgerStore _ledger;
    private readonly ProcessingJobStore _jobs;
    private readonly RejectPacing _pacing;
    private readonly TimeProvider _time;
    private readonly ILogger<ProcessingRejectHandler> _logger;

    public ProcessingRejectHandler(
        SqliteDatabase database,
        IMediaManagerPorts ports,
        MediaManagerConnectionService connections,
        HandoffCompletionReporter reporter,
        HandoffLedgerStore ledger,
        ProcessingJobStore jobs,
        RejectPacing pacing,
        TimeProvider time,
        ILogger<ProcessingRejectHandler> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _pacing = pacing ?? throw new ArgumentNullException(nameof(pacing));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobKind => IntakeRules.RejectJobKind;

    private sealed record ReportEnvelope(HandoffReportTarget Target, PyDict Body, HandoffReportDelivery Delivery);

    private sealed record RejectAttempt(bool Done, string Reason, string? Manager = null, PyDict? Detail = null, ReportEnvelope? Report = null)
    {
        public PyDict DetailOrEmpty => Detail ?? new PyDict();
    }

    public async Task HandleAsync(JobWorkContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var payload = FollowUpJobPayload.Parse(context.PayloadJson);
        var relativePath = FollowUpJobPayload.RelativeMediaPath(payload);
        var libraryId = FollowUpJobPayload.LibraryId(payload);
        if (relativePath.Length == 0 || libraryId is null)
        {
            throw new InvalidOperationException("A reject job needs a file and a library.");
        }

        var originRaw = FollowUpJobPayload.Origin(payload);
        var reason = payload.Get("reason") is PyStr reasonValue && reasonValue.Value.Length > 0
            ? reasonValue.Value
            : "Weir could not process this file.";
        var failureClass = payload.Get("failure_class") is PyStr failureClassValue ? failureClassValue.Value : null;
        var origin = originRaw is not null ? HandoffOrigin.FromPayload(new PyDict().Set("origin", originRaw)) : null;

        // 1. Everything the attempt needs, read with the unit of work closed before any network call.
        string watchedRoot = string.Empty;
        HandoffReportTarget? target = null;
        string? targetRefusalReason = null;
        var queueConnections = new List<ManagerConnection>();
        await LockedWrites.RunAsync(
            _database,
            async uow =>
            {
                var library = await RemuxPassHandler.ResolveLibraryAsync(uow, libraryId, null).ConfigureAwait(false);
                if (library is null)
                {
                    throw new InvalidOperationException($"Library {libraryId} no longer exists.");
                }

                watchedRoot = RemuxPassPaths.Resolve(library.WatchedFolder);
                if (origin is not null && _ports.PortForKind(origin.SourceKey) is { } originPort && !originPort.Capabilities().RemovesQueueItems)
                {
                    (target, targetRefusalReason) = await _reporter.ResolveHandoffTargetAsync(uow, origin).ConfigureAwait(false);
                }

                var connectionIds = await LibraryStore.ManagerConnectionIdsAsync(uow, library.Id).ConfigureAwait(false);
                var linked = await _connections.ConnectionsByIdAsync(uow, connectionIds).ConfigureAwait(false);
                queueConnections = [.. linked.Where(connection => _ports.PortForKind(connection.Kind)?.Capabilities().RemovesQueueItems == true)];
            },
            _logger,
            "reject claim",
            cancellationToken).ConfigureAwait(false);

        var source = RemuxPassPaths.Resolve(Path.Combine(watchedRoot, relativePath));

        // 2. The attempt, paced, with no unit of work open.
        await _pacing.WaitAsync(cancellationToken).ConfigureAwait(false);
        RejectAttempt attempt;
        if (origin is not null)
        {
            attempt = target is not null
                ? await RejectThroughHandoffAsync(target, origin, source, watchedRoot, reason, failureClass, cancellationToken).ConfigureAwait(false)
                : targetRefusalReason is not null
                    ? new RejectAttempt(false, $"Weir could not report to the manager: {targetRefusalReason}.")
                    : queueConnections.Count > 0
                        ? await RejectThroughQueueAsync(queueConnections, source, cancellationToken).ConfigureAwait(false)
                        : new RejectAttempt(false, "No linked media manager can take a rejection for this file, so Weir handed the original back instead.");
        }
        else if (queueConnections.Count > 0)
        {
            attempt = await RejectThroughQueueAsync(queueConnections, source, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            attempt = new RejectAttempt(false, "No linked media manager can take a rejection for this file, so Weir handed the original back instead.");
        }

        // 3. Bookkeeping, and the fallback, in one short transaction.
        var now = _time.GetUtcNow();
        await LockedWrites.RunAsync(
            _database,
            async uow =>
            {
                if (attempt.Report is { } report)
                {
                    await HandoffCompletionReporter.RecordHandoffReportAsync(uow, report.Target, report.Body, report.Delivery, relativePath).ConfigureAwait(false);
                }

                var detail = new PyDict()
                    .Set("job_id", context.Id)
                    .Set("relative_media_path", relativePath)
                    .Set("library_id", libraryId)
                    .Set("manager", attempt.Manager)
                    .Set("message", attempt.Reason)
                    .Set("trigger", "worker")
                    .Set("result", attempt.Done ? "success" : "warning");
                foreach (var (key, value) in attempt.DetailOrEmpty.Items)
                {
                    detail.Set(key, value);
                }

                string eventType;
                string title;
                if (attempt.Done)
                {
                    // Fix #532: always upsert, even when no scan has recorded this file yet. The reason keeps why
                    // the release was rejected (from the job payload — e.g. "no retainable audio") alongside what
                    // happened to the download, rather than the acceptance sentence alone replacing it.
                    var rejectedReason = PyStrings.Slice(PyStrings.Strip($"{reason} {attempt.Reason}"), 10_000);
                    await RemuxPassFileState.UpsertRejectedAsync(uow, libraryId.Value, relativePath, rejectedReason, failureClass).ConfigureAwait(false);
                    if (origin is not null)
                    {
                        await _ledger.RecordOutcomeAsync(uow, origin.SourceKey, origin.HandoffId, HandoffLedgerRules.Rejected, null, attempt.Reason).ConfigureAwait(false);
                    }

                    eventType = ActivityEventTypes.ProcessingFileRejected;
                    title = $"{MediaPathNames.Name(relativePath, OperatingSystem.IsWindows())} was rejected so a different release can be found";
                }
                else
                {
                    var liveLibrary = await RemuxPassHandler.ResolveLibraryAsync(uow, libraryId, null).ConfigureAwait(false);
                    if (liveLibrary is not null)
                    {
                        EnqueuePassThroughFallback(uow, liveLibrary, relativePath, originRaw);
                    }

                    await RemuxPassFileState.MarkFileStatusAsync(
                        uow, libraryId.Value, relativePath, ProcessingFileStatuses.ProcessingFailed,
                        PyStrings.Slice($"{reason} {attempt.Reason} Weir is handing the original back unchanged instead.", 10_000),
                        now).ConfigureAwait(false);
                    eventType = ActivityEventTypes.ProcessingFileRejectFellBack;
                    title = $"{MediaPathNames.Name(relativePath, OperatingSystem.IsWindows())} could not be rejected, so it is being handed back";
                    detail.Set("next_action", "Weir queued the original to be handed back unchanged.");
                }

                await SqliteActivityWriter.RecordAsync(uow, new ActivityEventDraft(
                    eventType, "processing", title, PyStrings.Slice(PyJsonWriter.Dumps(detail, PyJsonFormat.Compact), 10_000))).ConfigureAwait(false);
            },
            _logger,
            "reject bookkeeping",
            cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Reject for {Path}: {Reason}", relativePath, attempt.Reason);
    }

    /// <summary>Queues a pass-through, called unconditionally on any reject failure — the reject route never
    /// re-checks the library's failure policy: anything short of certainty always falls back to pass-through.</summary>
    private void EnqueuePassThroughFallback(UnitOfWork uow, ProcessingLibraryRecord library, string relativePath, PyDict? origin)
    {
        var body = new PyDict().Set("relative_media_path", relativePath).Set("library_id", library.Id).Set("trigger", "worker");
        if (origin is { IsTruthy: true })
        {
            body.Set("origin", origin);
        }

        // The fingerprint stats the file on the watched folder, which may be a network share. Work the
        // dedupe key out before asking for the transaction, not in the argument list after it: arguments
        // evaluate left to right, so inlining this would put a remote stat inside the write lock
        // (#586 made that lock start at BEGIN). QueueingFailurePolicy.Enqueue's callers do the same.
        var dedupeKey = $"{IntakeRules.PassThroughJobKind}:{library.Id}:{relativePath}:{QueueingFailurePolicy.FingerprintTag(library, relativePath)}";
        _jobs.EnqueueOrGet(
            uow.Connection,
            uow.WriteTransaction(),
            dedupeKey,
            IntakeRules.PassThroughJobKind,
            PyJsonWriter.Dumps(body, PyJsonFormat.Compact),
            JobQueueRules.DefaultMaxAttempts,
            0,
            (int)Math.Clamp(library.Priority, int.MinValue, int.MaxValue));
    }
}
