using System.Globalization;
using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Core.Processing.RemuxPass;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>
/// The Files-row and processing-record writes a pass makes (status, failure, measured media facts, output collision and
/// file log), each inside the caller's unit of work.
/// </summary>
public static class RemuxPassFileState
{
    private sealed record FileRow(long Id, string StatusReason, string? FailureClass, long FailureAttempts);

    private static Task<FileRow?> FindAsync(UnitOfWork uow, long libraryId, string relativePath) =>
        uow.QuerySingleAsync(
            "SELECT id, status_reason, failure_class, failure_attempts FROM files WHERE library_id = $library AND relative_path = $path LIMIT 1",
            reader => new FileRow(reader.GetInt64(0), SqliteValues.GetString(reader, 1), SqliteValues.GetStringOrNull(reader, 2), SqliteValues.GetInt64(reader, 3)),
            ("$library", libraryId),
            ("$path", relativePath));

    /// <summary>Moves a file Weir has already seen into a new state. False when there is no row.</summary>
    public static async Task<bool> MarkFileStatusAsync(UnitOfWork uow, long libraryId, string relativePath, string status, string reason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var row = await FindAsync(uow, libraryId, relativePath).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        var sets = new List<string> { "status = $status", "status_reason = $reason", "updated_at = CURRENT_TIMESTAMP" };
        if (status is ProcessingFileStatuses.Processing or ProcessingFileStatuses.Processed or ProcessingFileStatuses.ProcessingFailed)
        {
            sets.Add("last_attempt_at = $now");
        }

        if (status != ProcessingFileStatuses.BlockedUpstream)
        {
            sets.Add("blocked_by_connection = NULL");
        }

        if (status != ProcessingFileStatuses.OnHold)
        {
            sets.Add("hold_until = NULL");
        }

        await uow.ExecuteAsync(
            $"UPDATE files SET {string.Join(", ", sets)} WHERE id = $id",
            ("$status", status),
            ("$reason", reason),
            ("$now", TimestampColumns.Orm(now)),
            ("$id", row.Id)).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// A rejection always upserts a Files row, whether or not a scan had already seen the file, so a hand-off rejected
    /// before any watched-folder scan still appears on the Files screen (#532). Sets status <c>rejected</c>, the reason and the failure class; clears
    /// <c>blocked_by_connection</c> and <c>hold_until</c> the same way <see cref="MarkFileStatusAsync"/> does for any status
    /// that is neither <c>blocked_upstream</c> nor <c>on_hold</c>.
    /// </summary>
    public static async Task UpsertRejectedAsync(UnitOfWork uow, long libraryId, string relativePath, string reason, string? failureClass)
    {
        ArgumentNullException.ThrowIfNull(uow);
        if (await FindAsync(uow, libraryId, relativePath).ConfigureAwait(false) is null)
        {
            await uow.ExecuteAsync(
                "INSERT INTO files (library_id, relative_path) VALUES ($library, $path)",
                ("$library", libraryId),
                ("$path", relativePath)).ConfigureAwait(false);
        }

        await uow.ExecuteAsync(
            "UPDATE files SET status = $status, status_reason = $reason, failure_class = $class, " +
            "blocked_by_connection = NULL, hold_until = NULL, updated_at = CURRENT_TIMESTAMP " +
            "WHERE library_id = $library AND relative_path = $path",
            ("$status", ProcessingFileStatuses.Rejected),
            ("$reason", WireStrings.Slice(reason, 10000)),
            ("$class", failureClass),
            ("$library", libraryId),
            ("$path", relativePath)).ConfigureAwait(false);
    }

    /// <summary>The four failure fields cleared, after a success, a wait or a content rejection.</summary>
    public static Task ClearFailureFieldsAsync(UnitOfWork uow, long libraryId, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.ExecuteAsync(
            "UPDATE files SET failure_class = NULL, failure_attempts = 0, next_retry_at = NULL, hold_until = NULL, updated_at = CURRENT_TIMESTAMP " +
            "WHERE library_id = $library AND relative_path = $path",
            ("$library", libraryId),
            ("$path", relativePath));
    }

    /// <summary>
    /// Marks a file failed, classifies it and applies the retry policy. The decision's sentence becomes the
    /// reason on the record, so the screen says whether a retry is coming.
    /// </summary>
    public static async Task<RetryDecision> RecordFailureAsync(UnitOfWork uow, ProcessingLibraryRecord library, string relativePath, string failureClass, string reason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(library);
        var row = await FindAsync(uow, library.Id, relativePath).ConfigureAwait(false);
        if (row is null)
        {
            await uow.ExecuteAsync(
                "INSERT INTO files (library_id, relative_path) VALUES ($library, $path)",
                ("$library", library.Id),
                ("$path", relativePath)).ConfigureAwait(false);
            row = (await FindAsync(uow, library.Id, relativePath).ConfigureAwait(false))!;
        }

        var value = WireStrings.Strip(failureClass).ToLowerInvariant();
        var decision = RetryPolicy.DecideForRecordedFailure(library, value, row.FailureAttempts, row.FailureClass, now);
        await uow.ExecuteAsync(
            "UPDATE files SET status = $status, status_reason = $reason, failure_class = $class, failure_attempts = $attempts, " +
            "next_retry_at = $next, last_attempt_at = $now, updated_at = CURRENT_TIMESTAMP WHERE id = $id",
            ("$status", decision.Quarantined ? ProcessingFileStatuses.OnHold : ProcessingFileStatuses.ProcessingFailed),
            ("$reason", WireStrings.Slice(WireStrings.Strip($"{reason} {decision.Reason}"), 10000)),
            ("$class", value),
            ("$attempts", row.FailureAttempts + 1),
            ("$next", decision.NextRetryAt is { } next ? TimestampColumns.Orm(next) : null),
            ("$now", TimestampColumns.Orm(now)),
            ("$id", row.Id)).ConfigureAwait(false);
        return decision;
    }

    /// <summary>
    /// The size and modification time of the source a successful pass cleaned (migration 0010), or null for both when the
    /// pass did not measure it. The watched-folder scan compares them with the file on disk for a library that keeps
    /// originals (<see cref="Weir.Core.Processing.ProcessedSourceRules"/>).
    /// </summary>
    public static Task RecordProcessedSourceAsync(UnitOfWork uow, long libraryId, string relativePath, long? sizeBytes, long? modifiedTimeNs) =>
        uow.ExecuteAsync(
            "UPDATE files SET processed_source_size = $size, processed_source_mtime_ns = $mtime WHERE library_id = $library AND relative_path = $path",
            ("$size", sizeBytes),
            ("$mtime", sizeBytes is null ? null : modifiedTimeNs),
            ("$library", libraryId),
            ("$path", relativePath));

    /// <summary>The row's reason, or null when there is no row.</summary>
    public static async Task<string?> StatusReasonAsync(UnitOfWork uow, long libraryId, string relativePath) =>
        (await FindAsync(uow, libraryId, relativePath).ConfigureAwait(false))?.StatusReason;

    /// <summary><c>row.status_reason = f"{row.status_reason} {sentence}".strip()[:10000]</c>.</summary>
    public static async Task AppendStatusReasonAsync(UnitOfWork uow, long libraryId, string relativePath, string sentence)
    {
        var row = await FindAsync(uow, libraryId, relativePath).ConfigureAwait(false);
        if (row is null)
        {
            return;
        }

        await uow.ExecuteAsync(
            "UPDATE files SET status_reason = $reason, updated_at = CURRENT_TIMESTAMP WHERE id = $id",
            ("$reason", WireStrings.Slice(WireStrings.Strip($"{row.StatusReason} {sentence}"), 10000)),
            ("$id", row.Id)).ConfigureAwait(false);
    }

    /// <summary>
    /// Records measured media facts on the row matched by path within the pass's own library, not on every library that
    /// shares the path (#545); a value not measured leaves the column alone. A null
    /// <see cref="MeasuredMediaFacts.LibraryId"/> updates nothing rather than matching across libraries.
    /// </summary>
    public static Task RecordMeasuredMediaFactsAsync(UnitOfWork uow, MeasuredMediaFacts facts)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.LibraryId is not { } libraryId)
        {
            return Task.CompletedTask;
        }

        var sets = new List<string>();
        var parameters = new List<(string, object?)> { ("$path", facts.RelativePath), ("$library", libraryId) };
        void Add(string column, object? value)
        {
            if (value is null)
            {
                return;
            }

            sets.Add($"{column} = ${column}");
            parameters.Add(($"${column}", value));
        }

        Add("video_width", facts.VideoWidth);
        Add("video_height", facts.VideoHeight);
        Add("video_codec", facts.VideoCodec is null ? null : WireStrings.Slice(facts.VideoCodec, 64));
        Add("audio_track_count", facts.AudioTrackCount);
        Add("subtitle_track_count", facts.SubtitleTrackCount);
        Add("duration_seconds", facts.DurationSeconds);
        Add("audio_codecs", facts.AudioCodecs is null
            ? null
            : WireStrings.Slice(string.Join(",", facts.AudioCodecs.Where(c => WireStrings.Strip(c).Length > 0).Select(c => WireStrings.Strip(c).ToLowerInvariant())), 1000));
        Add("video_bit_depth", facts.VideoBitDepth);
        if (sets.Count == 0)
        {
            return Task.CompletedTask;
        }

        sets.Add("updated_at = CURRENT_TIMESTAMP");
        return uow.ExecuteAsync($"UPDATE files SET {string.Join(", ", sets)} WHERE relative_path = $path AND library_id = $library", [.. parameters]);
    }

    /// <summary>
    /// Keeps the output-collision decision on the pass's own library's row for the path, not on every library that
    /// shares the path (#545). A null <paramref name="libraryId"/> updates nothing.
    /// </summary>
    public static Task RecordOutputCollisionAsync(UnitOfWork uow, string relativePath, CollisionDecision decision, long? libraryId)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(decision);
        if (libraryId is not { } library)
        {
            return Task.CompletedTask;
        }

        return uow.ExecuteAsync(
            "UPDATE files SET output_collision_policy = $policy, output_collision_action = $action, output_collision_reason = $reason, " +
            "updated_at = CURRENT_TIMESTAMP WHERE relative_path = $path AND library_id = $library",
            ("$policy", decision.Policy),
            ("$action", decision.Action),
            ("$reason", decision.Reason),
            ("$path", relativePath),
            ("$library", library));
    }

    /// <summary>Records one completed pass in the file log, bounded, beside the file and library it belongs to.</summary>
    public static async Task RecordFileLogAsync(UnitOfWork uow, string relativePath, string title, WireObject detail, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(detail);
        WireValue payload = detail;
        var text = WireJsonWriter.Dumps(payload, WireJsonFormat.Response);
        if (WireStrings.Length(text) > FileLogStore.MaxDetailChars)
        {
            payload = new WireObject()
                .Set("truncated", true)
                .Set("truncated_note", $"This record was longer than {FileLogStore.MaxDetailChars.ToString(CultureInfo.InvariantCulture)} characters and was shortened when it was saved.")
                .Set("outcome", detail.Get("outcome") ?? WireNull.Instance)
                .Set("detail_excerpt", WireStrings.Slice(text, FileLogStore.MaxDetailChars / 2));
            text = WireJsonWriter.Dumps(payload, WireJsonFormat.Response);
        }

        var file = await uow.QuerySingleAsync(
            "SELECT f.id, l.id, l.name FROM files f LEFT JOIN libraries l ON l.id = f.library_id WHERE f.relative_path = $path ORDER BY f.id LIMIT 1",
            reader => new object?[] { reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetString(2) },
            ("$path", relativePath)).ConfigureAwait(false);
        var outcome = detail.Get("outcome") is { IsTruthy: true } value ? WireConvert.Str(value) : string.Empty;
        await uow.ExecuteAsync(
            "INSERT INTO file_logs (file_id, library_id, relative_path, library_name, outcome, title, detail_json, recorded_at) " +
            "VALUES ($file, $library, $path, $name, $outcome, $title, $detail, $recorded)",
            ("$file", file?[0]),
            ("$library", file?[1]),
            ("$path", relativePath),
            ("$name", file?[2] as string ?? string.Empty),
            ("$outcome", outcome),
            ("$title", title),
            ("$detail", text),
            ("$recorded", TimestampColumns.Orm(now))).ConfigureAwait(false);
    }
}
