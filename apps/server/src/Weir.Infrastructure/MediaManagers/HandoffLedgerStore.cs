using System.Globalization;
using Microsoft.Data.Sqlite;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Core.Time;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>
/// One <c>media_manager_handoffs</c> row. <see cref="Outcome"/> is the manager's own word on what became of the file
/// (<c>imported</c> or <c>not-imported</c>, #652), with what Weir answered and whether it released its copy.
/// </summary>
public sealed record HandoffLedgerRow(
    long Id,
    string SourceKey,
    string HandoffId,
    long? LibraryId,
    string RelativePath,
    string State,
    string? OutputPath,
    string? Message,
    DateTimeOffset? LastChangedAt,
    string? Outcome = null,
    string? OutcomeMessage = null,
    bool OutcomeReleased = false,
    string? DownloadId = null,
    DateTimeOffset? ReceivedAt = null);

/// <summary>The <c>files</c> columns the ledger reads.</summary>
public sealed record HandoffFileRow(long Id, string RelativePath, string Status, string StatusReason, long FailureAttempts, DateTimeOffset? NextRetryAt, DateTimeOffset? UpdatedAt);

/// <summary>
/// The hand-off ledger: state is worked out live from the job queue and the Files rows
/// while they exist, and the row keeps the last answer so it survives job-row pruning.
/// </summary>
public sealed class HandoffLedgerStore
{
    private const string LedgerColumns =
        "id, source_key, handoff_id, library_id, relative_path, state, output_path, message, last_changed_at, outcome, outcome_message, " +
        "outcome_released, download_id, created_at";

    private readonly TimeProvider _time;

    public HandoffLedgerStore(TimeProvider time)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>The ledger row for a manager's hand-off id, or null.</summary>
    public static Task<HandoffLedgerRow?> FindAsync(UnitOfWork uow, string sourceKey, string handoffId)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.QuerySingleAsync(
            $"SELECT {LedgerColumns} FROM media_manager_handoffs WHERE source_key = $source AND handoff_id = $id LIMIT 1",
            ReadLedger,
            ("$source", sourceKey),
            ("$id", handoffId));
    }

    /// <summary>
    /// Record a hand-off at intake. A repeat of a finished hand-off starts it over, and so forgets what the
    /// manager said about the last copy (#652). The manager's download id, when it sent one, is kept so a Sonarr or Radarr
    /// import can be matched by it.
    /// </summary>
    public async Task RecordReceivedAsync(UnitOfWork uow, string sourceKey, string handoffId, long? libraryId, string relativePath, string? downloadId = null)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var now = PythonTimestamps.Orm(_time.GetUtcNow());
        var row = await FindAsync(uow, sourceKey, handoffId).ConfigureAwait(false);
        if (row is null)
        {
            await uow.ExecuteAsync(
                "INSERT INTO media_manager_handoffs (source_key, handoff_id, library_id, relative_path, state, output_path, message, created_at, last_changed_at, download_id) " +
                "VALUES ($source, $id, $library, $path, $state, NULL, NULL, $now, $now, $download)",
                ("$source", sourceKey),
                ("$id", handoffId),
                ("$library", libraryId),
                ("$path", relativePath),
                ("$state", HandoffLedgerRules.Queued),
                ("$now", now),
                ("$download", string.IsNullOrWhiteSpace(downloadId) ? null : downloadId.Trim())).ConfigureAwait(false);
        }
        else if (HandoffLedgerRules.TerminalStates.Contains(row.State))
        {
            await uow.ExecuteAsync(
                "UPDATE media_manager_handoffs SET library_id = $library, relative_path = $path, state = $state, output_path = NULL, " +
                "message = NULL, last_changed_at = $now, outcome = NULL, outcome_at = NULL, outcome_message = NULL, outcome_released = 0, " +
                "pending_report_json = NULL, download_id = coalesce($download, download_id) WHERE id = $row",
                ("$library", libraryId),
                ("$path", relativePath),
                ("$state", HandoffLedgerRules.Queued),
                ("$now", now),
                ("$download", string.IsNullOrWhiteSpace(downloadId) ? null : downloadId.Trim()),
                ("$row", row.Id)).ConfigureAwait(false);
        }
    }

    /// <summary>The manager's own word on a finished hand-off (#652), and what Weir answered.</summary>
    public async Task RecordManagerOutcomeAsync(UnitOfWork uow, long rowId, string outcome, DateTimeOffset occurredAt, string message, bool released)
    {
        ArgumentNullException.ThrowIfNull(uow);
        await uow.ExecuteAsync(
            "UPDATE media_manager_handoffs SET outcome = $outcome, outcome_at = $at, outcome_message = $message, outcome_released = $released, " +
            "last_changed_at = $now WHERE id = $row",
            ("$outcome", outcome),
            ("$at", PythonTimestamps.Orm(occurredAt)),
            ("$message", PyStrings.Slice(message, 2000)),
            ("$released", released ? 1 : 0),
            ("$now", PythonTimestamps.Orm(_time.GetUtcNow())),
            ("$row", rowId)).ConfigureAwait(false);
    }

    /// <summary>Hand-offs whose manager gave this download id, newest first.</summary>
    public static Task<List<HandoffLedgerRow>> WithDownloadIdAsync(UnitOfWork uow, string downloadId)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.QueryAsync(
            $"SELECT {LedgerColumns} FROM media_manager_handoffs WHERE download_id = $download ORDER BY id DESC",
            ReadLedger,
            ("$download", downloadId));
    }

    /// <summary>
    /// A report Weir owes the manager because it was not answering when the pass ended (null clears it). The heartbeat
    /// sends it once the manager answers (<see cref="HandoffCompletionReporter.SendWaitingReportsAsync"/>).
    /// </summary>
    public static Task SetPendingReportAsync(UnitOfWork uow, string sourceKey, string handoffId, string? reportJson)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.ExecuteAsync(
            "UPDATE media_manager_handoffs SET pending_report_json = $report WHERE source_key = $source AND handoff_id = $id",
            ("$report", reportJson),
            ("$source", sourceKey),
            ("$id", handoffId));
    }

    /// <summary>Every report Weir still owes a manager of this kind: the hand-off id and the saved report.</summary>
    public static Task<List<(string HandoffId, string ReportJson)>> PendingReportsAsync(UnitOfWork uow, string sourceKey)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.QueryAsync(
            "SELECT handoff_id, pending_report_json FROM media_manager_handoffs WHERE source_key = $source AND pending_report_json IS NOT NULL ORDER BY id",
            reader => (SqliteValues.GetString(reader, 0), SqliteValues.GetString(reader, 1)),
            ("$source", sourceKey));
    }

    /// <summary>Record a result Weir reached. Unknown and cancelled hand-offs are left alone.</summary>
    public async Task RecordOutcomeAsync(UnitOfWork uow, string sourceKey, string? handoffId, string state, string? outputPath = null, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(uow);
        if (string.IsNullOrEmpty(handoffId))
        {
            return;
        }

        var row = await FindAsync(uow, sourceKey, handoffId).ConfigureAwait(false);
        if (row is null || row.State == HandoffLedgerRules.Cancelled)
        {
            return;
        }

        if (row.State != state || row.OutputPath != outputPath)
        {
            await uow.ExecuteAsync(
                "UPDATE media_manager_handoffs SET state = $state, output_path = $output, message = $message, last_changed_at = $now WHERE id = $row",
                ("$state", state),
                ("$output", outputPath),
                ("$message", string.IsNullOrEmpty(message) ? null : PyStrings.Slice(message, 2000)),
                ("$now", PythonTimestamps.Orm(_time.GetUtcNow())),
                ("$row", row.Id)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The file, or every file under the folder, this hand-off covers.
    /// Matching is an exact prefix compare in .NET, not SQL <c>LIKE</c> (#544 item 5): <c>LIKE</c> treats <c>_</c> and
    /// <c>%</c> in the path as wildcards, so a sibling folder whose name merely resembles this one would be folded into
    /// this hand-off's status, and it ignores case on every platform. Case follows OS path semantics, as elsewhere
    /// (<see cref="ReconciliationService.SafeUnlinkUnderRoots"/>, <see cref="HandoffCompletionReporter.TranslateOutputPath"/>):
    /// case-insensitive on Windows, case-sensitive everywhere else.
    /// </summary>
    public static async Task<List<HandoffFileRow>> FileRowsAsync(UnitOfWork uow, HandoffLedgerRow row)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(row);
        if (row.LibraryId is not { } libraryId)
        {
            return [];
        }

        var path = row.RelativePath.TrimEnd('/');
        var rows = await uow.QueryAsync(
            "SELECT id, relative_path, status, status_reason, failure_attempts, next_retry_at, updated_at FROM files " +
            "WHERE library_id = $library",
            reader => new HandoffFileRow(
                SqliteValues.GetInt64(reader, 0),
                SqliteValues.GetString(reader, 1),
                SqliteValues.GetString(reader, 2),
                SqliteValues.GetString(reader, 3),
                SqliteValues.GetInt64(reader, 4),
                PythonTimestamps.Parse(reader.GetValue(5)),
                PythonTimestamps.Parse(reader.GetValue(6))),
            ("$library", libraryId)).ConfigureAwait(false);

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = path + "/";
        return [.. rows.Where(file => string.Equals(file.RelativePath, path, comparison) || file.RelativePath.StartsWith(prefix, comparison))];
    }

    /// <summary>
    /// Pending or leased jobs keyed to this hand-off, or the failure-policy jobs for its files.
    /// The hand-off's own <c>dedupe_key</c> prefix is matched with <c>substr(...) =</c>, not <c>LIKE</c> (#544 item 5), so a
    /// hand-off id containing <c>_</c> or <c>%</c> cannot match another hand-off's jobs. A dedupe key is an opaque
    /// identifier, not a path, so this match is always case-sensitive (SQLite's default <c>BINARY</c> collation for <c>=</c>).
    /// </summary>
    public static async Task<List<ProcessingJob>> JobsForAsync(UnitOfWork uow, HandoffLedgerRow row)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(row);
        var baseKey = IntakeRules.RemuxDedupeKey(row.SourceKey, row.HandoffId);
        var conditions = new List<string> { "dedupe_key = $base", "substr(dedupe_key, 1, length($base_prefix)) = $base_prefix" };
        var parameters = new List<(string, object?)> { ("$base", baseKey), ("$base_prefix", baseKey + ":") };
        if (row.LibraryId is { } libraryId)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal) { row.RelativePath };
            foreach (var file in await FileRowsAsync(uow, row).ConfigureAwait(false))
            {
                paths.Add(file.RelativePath);
            }

            var index = 0;
            foreach (var path in paths)
            {
                // #545 item 2: pass-through and reject dedupe keys carry the source's fingerprint as a trailing segment
                // (so a later failure of a since-replaced file queues again), so the ledger matches the base
                // "{kind}:{library}:{path}" either exactly (rows written by earlier releases) or as a prefix. The prefix is
                // compared as plain text, not a LIKE pattern, so "_" or "%" in a path never matches another file (#544 item 5).
                var passBase = $"{IntakeRules.PassThroughJobKind}:{libraryId.ToString(CultureInfo.InvariantCulture)}:{path}";
                var rejectBase = $"{IntakeRules.RejectJobKind}:{libraryId.ToString(CultureInfo.InvariantCulture)}:{path}";
                conditions.Add($"(dedupe_key = $pass_{index} OR substr(dedupe_key, 1, length($pass_prefix_{index})) = $pass_prefix_{index})");
                parameters.Add(($"$pass_{index}", passBase));
                parameters.Add(($"$pass_prefix_{index}", passBase + ":"));
                conditions.Add($"(dedupe_key = $reject_{index} OR substr(dedupe_key, 1, length($reject_prefix_{index})) = $reject_prefix_{index})");
                parameters.Add(($"$reject_{index}", rejectBase));
                parameters.Add(($"$reject_prefix_{index}", rejectBase + ":"));
                index++;
            }
        }

        parameters.Add(("$pending", ProcessingJobStatus.Pending));
        parameters.Add(("$leased", ProcessingJobStatus.Leased));
        parameters.Add(("$failed", ProcessingJobStatus.Failed));
        parameters.Add(("$pass_kind", IntakeRules.PassThroughJobKind));
        parameters.Add(("$reject_kind", IntakeRules.RejectJobKind));
        // #545 item 3: a pass-through or reject job that exhausted its own retries drops out of pending/leased, but it
        // is still an undelivered outcome the manager needs to hear about (as failed, with the reason) rather than
        // silently vanishing from the ledger's view.
        return await uow.QueryAsync(
            $"SELECT {ProcessingJobStore.JobColumns} FROM jobs WHERE ({string.Join(" OR ", conditions)}) AND " +
            "(status IN ($pending, $leased) OR (status = $failed AND job_kind IN ($pass_kind, $reject_kind)))",
            ProcessingJobStore.ReadJob,
            [.. parameters]).ConfigureAwait(false);
    }

    /// <summary>
    /// Files waiting ahead of this one, plus one. Only file work counts (another file's pass or a library clean), because
    /// that is what a manager is waiting behind. Background jobs (folder scans, the work file sweep) are quick and are not
    /// files; counting them would tell a manager it was third in line behind two sweeps.
    /// </summary>
    public static async Task<long> QueuePositionAsync(UnitOfWork uow, ProcessingJob job)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(job);
        var ahead = await uow.CountAsync(
            "SELECT count(id) FROM jobs WHERE status = $pending AND (priority > $priority OR (priority = $priority AND id < $id)) " +
            "AND (job_kind LIKE 'processing.file.%' OR job_kind = $library_clean)",
            ("$pending", ProcessingJobStatus.Pending),
            ("$priority", job.Priority),
            ("$id", job.Id),
            ("$library_clean", "processing.library.clean.v1")).ConfigureAwait(false);
        return ahead + 1;
    }

    /// <summary>Work the state out from what exists now, and keep the ledger row in step with it.</summary>
    public async Task<HandoffStatus> CurrentStatusAsync(UnitOfWork uow, HandoffLedgerRow row)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(row);
        var now = PyDateTimeNow();
        string? liveState = null;
        DateTimeOffset? changedAt = null;
        long? queuePosition = null;
        DateTimeOffset? scheduledFor = null;
        string? message = null;

        if (row.State != HandoffLedgerRules.Cancelled)
        {
            var jobs = await JobsForAsync(uow, row).ConfigureAwait(false);
            var files = await FileRowsAsync(uow, row).ConfigureAwait(false);
            var states = new List<string>();
            var stamps = new List<DateTimeOffset>();
            var busyPaths = new HashSet<string>(StringComparer.Ordinal);

            foreach (var job in jobs)
            {
                stamps.Add(job.UpdatedAt);
                if (PayloadRelativePath(job.PayloadJson) is { } busyPath)
                {
                    busyPaths.Add(busyPath);
                }

                var isOutcomeJob = job.JobKind is IntakeRules.PassThroughJobKind or IntakeRules.RejectJobKind;

                // #545 item 3: a pass-through or reject job that exhausted its own retries is a final, undelivered
                // outcome — report it as failed, with the reason the job itself recorded, rather than let it vanish
                // once it drops out of pending/leased (JobsForAsync still returns it for exactly this reason).
                if (job.Status == ProcessingJobStatus.Failed)
                {
                    // A failed pass-through or reject job is found by the file's path, not by the hand-off, so one left
                    // by an earlier hand-off of the same release belongs to that hand-off, not this one. Counting it
                    // would turn a hand-off Weir had just completed into "failed", and Weir would then refuse the
                    // manager's "imported" for it.
                    if (IsFromEarlierHandoff(row, job.CreatedAt))
                    {
                        continue;
                    }

                    states.Add(HandoffLedgerRules.Failed);
                    message ??= string.IsNullOrEmpty(job.LastError) ? "Weir could not hand this file back to your media manager." : job.LastError;
                    continue;
                }

                if (job.Status == ProcessingJobStatus.Leased)
                {
                    states.Add(HandoffLedgerRules.Working);
                    continue;
                }

                if (isOutcomeJob)
                {
                    // A pending pass-through or reject job is a decided disposition about to run, not a normal place
                    // in the remux queue, so it is reported as scheduled rather than queued (and carries no queue
                    // position — that field means something only for the remux queue itself).
                    states.Add(HandoffLedgerRules.Scheduled);
                    if (job.NotBefore is { } outcomeNotBefore && outcomeNotBefore > now)
                    {
                        scheduledFor = scheduledFor is { } currentOutcome ? Min(currentOutcome, outcomeNotBefore) : outcomeNotBefore;
                    }

                    continue;
                }

                if (job.NotBefore is { } notBefore && notBefore > now)
                {
                    states.Add(HandoffLedgerRules.Scheduled);
                    scheduledFor = scheduledFor is { } current ? Min(current, notBefore) : notBefore;
                }
                else
                {
                    states.Add(HandoffLedgerRules.Queued);
                    var position = await QueuePositionAsync(uow, job).ConfigureAwait(false);
                    queuePosition = queuePosition is { } currentPosition && currentPosition != 0 ? Math.Min(currentPosition, position) : position;
                }
            }

            foreach (var file in files)
            {
                if (file.UpdatedAt is { } stamp)
                {
                    stamps.Add(stamp);
                }

                if (busyPaths.Contains(file.RelativePath))
                {
                    if (file.StatusReason.Length > 0 && message is null)
                    {
                        message = file.StatusReason;
                    }

                    continue;
                }

                var (state, when) = HandoffLedgerRules.FileState(file.Status, file.NextRetryAt, file.FailureAttempts);

                // The same for a file row: a failure recorded before this hand-off arrived, and not touched since, is
                // what became of an earlier hand-off of the path. A success from before still counts.
                if (state is HandoffLedgerRules.Failed or HandoffLedgerRules.Rejected or HandoffLedgerRules.Cancelled &&
                    IsFromEarlierHandoff(row, file.UpdatedAt))
                {
                    continue;
                }

                states.Add(state);
                if (when is { } whenValue)
                {
                    scheduledFor = scheduledFor is { } current ? Min(current, whenValue) : whenValue;
                }

                if (file.StatusReason.Length > 0 && (message is null || !HandoffLedgerRules.TerminalStates.Contains(state)))
                {
                    message = file.StatusReason;
                }
            }

            if (states.Count > 0)
            {
                liveState = HandoffLedgerRules.Combine(states);
                changedAt = stamps.Count > 0 ? stamps.Max() : null;
                if (liveState != HandoffLedgerRules.Queued)
                {
                    queuePosition = null;
                }

                if (liveState != HandoffLedgerRules.Scheduled)
                {
                    scheduledFor = null;
                }
            }
        }

        var answerState = row.State;
        var lastChanged = row.LastChangedAt;
        var storedMessage = row.Message;
        if (liveState is not null)
        {
            var changes = new List<(string, object?)>();
            var previous = row.LastChangedAt;
            if (liveState != row.State)
            {
                answerState = liveState;
                lastChanged = changedAt is { } changed && (previous is null || changed > previous) ? changedAt : now;
                changes.Add(("state", answerState));
                changes.Add(("last_changed_at", PythonTimestamps.Orm(lastChanged!.Value)));
            }
            else if (changedAt is { } newer && previous is { } before && newer > before)
            {
                lastChanged = newer;
                changes.Add(("last_changed_at", PythonTimestamps.Orm(newer)));
            }

            if (message is not null && !HandoffLedgerRules.TerminalStates.Contains(liveState))
            {
                var sliced = PyStrings.Slice(message, 2000);
                if (sliced != row.Message)
                {
                    storedMessage = sliced;
                    changes.Add(("message", sliced));
                }
            }

            if (changes.Count > 0)
            {
                var sets = changes.Select((change, index) => $"{change.Item1} = $v{index}");
                await uow.ExecuteAsync(
                    $"UPDATE media_manager_handoffs SET {string.Join(", ", sets)} WHERE id = $row",
                    [.. changes.Select((change, index) => ($"$v{index}", change.Item2)), ("$row", row.Id)]).ConfigureAwait(false);
            }
        }

        return new HandoffStatus(
            row.HandoffId,
            answerState,
            lastChanged ?? now,
            queuePosition,
            scheduledFor,
            answerState is HandoffLedgerRules.Completed or HandoffLedgerRules.PassedThrough ? row.OutputPath : null,
            message ?? storedMessage);
    }

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
                await FileStateStore.MarkCancelledAsync(uow, libraryId, file.RelativePath, CancelledFileReasons.ByManager).ConfigureAwait(false);
            }
        }

        await uow.ExecuteAsync(
            "UPDATE media_manager_handoffs SET state = $state, message = $message, last_changed_at = $now WHERE id = $row",
            ("$state", HandoffLedgerRules.Cancelled),
            ("$message", HandoffLedgerRules.CancelledMessage),
            ("$now", PythonTimestamps.Orm(_time.GetUtcNow())),
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
            ("$now", PythonTimestamps.Orm(_time.GetUtcNow())),
            ("$row", row.Id)).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// The <c>relative_media_path</c> a job payload names, as a string (empty when missing or falsy); null for unreadable
    /// JSON. A payload that is not a JSON object throws.
    /// </summary>
    private static string? PayloadRelativePath(string? payloadJson)
    {
        PyJson parsed;
        try
        {
            parsed = PyJsonParser.Parse(string.IsNullOrEmpty(payloadJson) ? "{}" : payloadJson);
        }
        catch (PyJsonDecodeException)
        {
            return null;
        }

        if (parsed is not PyDict dict)
        {
            throw new InvalidOperationException("A job's payload is not a JSON object.");
        }

        return dict.Get("relative_media_path") is { IsTruthy: true } value ? PyConvert.Str(value) : string.Empty;
    }

    private DateTimeOffset PyDateTimeNow() => PyDateTime.TruncateToMicroseconds(_time.GetUtcNow());

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a <= b ? a : b;

    /// <summary>
    /// How much older than the hand-off a record must be to belong to an earlier one. Receiving a hand-off writes its
    /// file's row in the same request, a moment before the hand-off's own row, so "older at all" would wrongly set
    /// aside a failure that happened to this hand-off. An earlier hand-off's leftovers are minutes to days old.
    /// </summary>
    private static readonly TimeSpan EarlierHandoffMargin = TimeSpan.FromMinutes(1);

    /// <summary>Whether a record last written at <paramref name="at"/> belongs to an earlier hand-off of the same path.</summary>
    internal static bool IsFromEarlierHandoff(HandoffLedgerRow row, DateTimeOffset? at) =>
        row.ReceivedAt is { } received && at is { } written && written < received - EarlierHandoffMargin;

    private static HandoffLedgerRow ReadLedger(SqliteDataReader reader) => new(
        SqliteValues.GetInt64(reader, 0),
        SqliteValues.GetString(reader, 1),
        SqliteValues.GetString(reader, 2),
        reader.IsDBNull(3) ? null : SqliteValues.GetInt64(reader, 3),
        SqliteValues.GetString(reader, 4),
        SqliteValues.GetString(reader, 5),
        SqliteValues.GetStringOrNull(reader, 6),
        SqliteValues.GetStringOrNull(reader, 7),
        PythonTimestamps.Parse(reader.GetValue(8)),
        SqliteValues.GetStringOrNull(reader, 9),
        SqliteValues.GetStringOrNull(reader, 10),
        SqliteValues.GetBool(reader, 11),
        SqliteValues.GetStringOrNull(reader, 12),
        PythonTimestamps.Parse(reader.GetValue(13)));
}
