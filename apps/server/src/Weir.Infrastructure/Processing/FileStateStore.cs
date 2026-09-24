using Microsoft.Data.Sqlite;
using Weir.Core.Processing;
using Weir.Core.Time;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>SQLite access for <c>files</c>: read, list and forget, plus the upsert and mark-status writes the
/// watched-folder scan performs.</summary>
public static class FileStateStore
{
    private const string Columns =
        "id, library_id, relative_path, status, status_reason, blocked_by_connection, size_bytes, video_width, video_height, " +
        "video_codec, audio_track_count, subtitle_track_count, duration_seconds, audio_codecs, video_bit_depth, size_changed_at, " +
        "hold_until, failure_class, failure_attempts, next_retry_at, output_collision_policy, output_collision_action, " +
        "output_collision_reason, hardware_method, hardware_fell_back_to_software, hardware_reason, last_seen_at, last_attempt_at, " +
        "created_at, updated_at, processed_source_size, processed_source_mtime_ns";

    public static Task<ProcessingFileRecord?> GetAsync(UnitOfWork uow, long id) =>
        uow.QuerySingleAsync($"SELECT {Columns} FROM files WHERE id = @id", Read, ("@id", id));

    public static Task<ProcessingFileRecord?> FindAsync(UnitOfWork uow, long libraryId, string relativePath) =>
        uow.QuerySingleAsync(
            $"SELECT {Columns} FROM files WHERE library_id = @lib AND relative_path = @path",
            Read, ("@lib", libraryId), ("@path", relativePath));

    /// <summary>The files matching <paramref name="filter"/>.</summary>
    public static Task<List<ProcessingFileRecord>> ListAsync(UnitOfWork uow, ProcessingFileListFilter filter)
    {
        var (sql, parameters) = ListQuery(filter);
        return uow.QueryAsync(sql, Read, parameters);
    }

    internal static (string Sql, (string Name, object? Value)[] Parameters) ListQuery(ProcessingFileListFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var clauses = new List<string>();
        var parameters = new List<(string, object?)>();
        if (filter.LibraryId is { } libraryId)
        {
            clauses.Add("library_id = @library_id");
            parameters.Add(("@library_id", libraryId));
        }

        if (!string.IsNullOrEmpty(filter.Status))
        {
            clauses.Add("status = @status");
            parameters.Add(("@status", filter.Status));
        }

        if (!string.IsNullOrEmpty(filter.PathContains))
        {
            clauses.Add("relative_path LIKE @path_contains ESCAPE '\\'");
            parameters.Add(("@path_contains", "%" + SqliteLike.Escape(filter.PathContains) + "%"));
        }

        if (filter.Since is { } since)
        {
            clauses.Add("last_seen_at >= @since");
            parameters.Add(("@since", SqliteValues.ToSqlite(since)));
        }

        var where = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : string.Empty;
        return ($"SELECT {Columns} FROM files {where} ORDER BY last_seen_at DESC, id DESC LIMIT {filter.ClampedLimit}", [.. parameters]);
    }

    /// <summary>A count per known status, zero-filled.</summary>
    public static async Task<Dictionary<string, long>> StatusCountsAsync(UnitOfWork uow, long? libraryId)
    {
        var counts = ProcessingFileStatuses.All.ToDictionary(s => s, _ => 0L, StringComparer.Ordinal);
        var sql = "SELECT status, COUNT(*) FROM files" + (libraryId is not null ? " WHERE library_id = @lib" : string.Empty) + " GROUP BY status";
        var rows = libraryId is { } id
            ? await uow.QueryAsync(sql, reader => (Status: reader.GetString(0), Count: reader.GetInt64(1)), ("@lib", id)).ConfigureAwait(false)
            : await uow.QueryAsync(sql, reader => (Status: reader.GetString(0), Count: reader.GetInt64(1))).ConfigureAwait(false);
        foreach (var (status, count) in rows)
        {
            if (counts.ContainsKey(status))
            {
                counts[status] = count;
            }
        }

        return counts;
    }

    /// <summary>Removes Weir's record; never touches the file on disk.</summary>
    public static Task ForgetAsync(UnitOfWork uow, long id) => uow.ExecuteAsync("DELETE FROM files WHERE id = @id", ("@id", id));

    /// <summary>
    /// A queued pass for this file was cancelled before Weir started on it (#643): the file reads cancelled, with
    /// <paramref name="reason"/>, and no retry is owed. Only a file still waiting, held or failed changes. One that is being
    /// processed, or already has an outcome, keeps it.
    /// </summary>
    public static Task MarkCancelledAsync(UnitOfWork uow, long libraryId, string relativePath, string reason)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.ExecuteAsync(
            "UPDATE files SET status = @cancelled, status_reason = @reason, next_retry_at = NULL, hold_until = NULL, " +
            "blocked_by_connection = NULL, updated_at = CURRENT_TIMESTAMP " +
            "WHERE library_id = @library AND relative_path = @path AND status IN (@unprocessed, @held, @outside, @blocked, @failed)",
            ("@cancelled", ProcessingFileStatuses.Cancelled),
            ("@reason", reason),
            ("@library", libraryId),
            ("@path", relativePath),
            ("@unprocessed", ProcessingFileStatuses.Unprocessed),
            ("@held", ProcessingFileStatuses.OnHold),
            ("@outside", ProcessingFileStatuses.OutOfSchedule),
            ("@blocked", ProcessingFileStatuses.BlockedUpstream),
            ("@failed", ProcessingFileStatuses.ProcessingFailed));
    }

    /// <summary>The row a previous scan left, or <see langword="null"/>. Settling compares against this. Same as
    /// <see cref="FindAsync"/>.</summary>
    public static Task<ProcessingFileRecord?> ExistingFileRowAsync(UnitOfWork uow, long libraryId, string relativePath) =>
        FindAsync(uow, libraryId, relativePath);

    /// <summary>
    /// Upserts one file's state. Safe to call on every scan. <paramref name="sizeBytes"/>
    /// and <paramref name="sizeChangedAt"/> of <see langword="null"/> mean "not supplied" (leave the
    /// stored value alone), distinct from a genuine zero.
    /// </summary>
    public static async Task<long> RecordFileStateAsync(
        UnitOfWork uow,
        long libraryId,
        string relativePath,
        FileStateVerdict verdict,
        long? sizeBytes,
        DateTimeOffset? sizeChangedAt,
        DateTimeOffset seenAt,
        bool isAttempt = false)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(verdict);
        var existingId = await uow.ScalarAsync(
            "SELECT id FROM files WHERE library_id = @lib AND relative_path = @path",
            ("@lib", libraryId), ("@path", relativePath)).ConfigureAwait(false);

        var seen = PyDateTime.FromDateTimeOffset(seenAt);
        var holdUntil = verdict.HoldUntil is { } hold ? PyDateTime.FromDateTimeOffset(hold) : (PyDateTime?)null;
        var changedAt = sizeChangedAt is { } changed ? PyDateTime.FromDateTimeOffset(changed) : (PyDateTime?)null;

        if (existingId is null or DBNull)
        {
            await uow.ExecuteAsync(
                "INSERT INTO files (library_id, relative_path, status, status_reason, blocked_by_connection, hold_until, " +
                "size_bytes, size_changed_at, last_seen_at, last_attempt_at) VALUES (@lib, @path, @status, @reason, @blocked, @hold, " +
                "@size, @size_changed, @seen, @attempt)",
                ("@lib", libraryId),
                ("@path", relativePath),
                ("@status", verdict.Status),
                ("@reason", verdict.Reason),
                ("@blocked", verdict.BlockedByConnection),
                ("@hold", SqliteValues.ToSqlite(holdUntil)),
                ("@size", sizeBytes ?? 0),
                ("@size_changed", SqliteValues.ToSqlite(changedAt)),
                ("@seen", SqliteValues.ToSqlite(seen)),
                ("@attempt", isAttempt ? SqliteValues.ToSqlite(seen) : null)).ConfigureAwait(false);
            return Convert.ToInt64(await uow.ScalarAsync(
                "SELECT id FROM files WHERE library_id = @lib AND relative_path = @path",
                ("@lib", libraryId), ("@path", relativePath)).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        }

        var id = Convert.ToInt64(existingId, System.Globalization.CultureInfo.InvariantCulture);
        var sets = new List<string>
        {
            "status = @status", "status_reason = @reason", "blocked_by_connection = @blocked", "hold_until = @hold",
            "last_seen_at = @seen", "updated_at = CURRENT_TIMESTAMP",
        };
        var parameters = new List<(string, object?)>
        {
            ("@status", verdict.Status),
            ("@reason", verdict.Reason),
            ("@blocked", verdict.BlockedByConnection),
            ("@hold", SqliteValues.ToSqlite(holdUntil)),
            ("@seen", SqliteValues.ToSqlite(seen)),
            ("@id", id),
        };
        if (sizeBytes is { } size)
        {
            sets.Add("size_bytes = @size");
            parameters.Add(("@size", size));
        }

        if (changedAt is { } changed2)
        {
            sets.Add("size_changed_at = @size_changed");
            parameters.Add(("@size_changed", SqliteValues.ToSqlite(changed2)));
        }

        if (isAttempt)
        {
            sets.Add("last_attempt_at = @attempt");
            parameters.Add(("@attempt", SqliteValues.ToSqlite(seen)));
        }

        await uow.ExecuteAsync($"UPDATE files SET {string.Join(", ", sets)} WHERE id = @id", [.. parameters]).ConfigureAwait(false);
        return id;
    }

    /// <summary>
    /// Records that a scan saw the row's file on disk at <paramref name="seenAt"/>, changing nothing else. The vanished-file
    /// sweep (#645) reads this to tell a file that is gone from one a scan simply left alone.
    /// </summary>
    public static async Task TouchLastSeenAsync(UnitOfWork uow, long fileId, DateTimeOffset seenAt)
    {
        ArgumentNullException.ThrowIfNull(uow);
        await uow.ExecuteAsync(
            "UPDATE files SET last_seen_at = @seen WHERE id = @id",
            ("@seen", SqliteValues.ToSqlite(PyDateTime.FromDateTimeOffset(seenAt))), ("@id", fileId)).ConfigureAwait(false);
    }

    /// <summary>Moves a file Weir has already seen into a new state. Does nothing when the row does not
    /// exist.</summary>
    public static async Task MarkFileStatusAsync(UnitOfWork uow, long libraryId, string relativePath, string status, string reason)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var sets = new List<string> { "status = @status", "status_reason = @reason", "updated_at = CURRENT_TIMESTAMP" };
        var parameters = new List<(string, object?)> { ("@status", status), ("@reason", reason), ("@lib", libraryId), ("@path", relativePath) };
        if (status is ProcessingFileStatuses.Processing or ProcessingFileStatuses.Processed or ProcessingFileStatuses.ProcessingFailed)
        {
            sets.Add("last_attempt_at = CURRENT_TIMESTAMP");
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
            $"UPDATE files SET {string.Join(", ", sets)} WHERE library_id = @lib AND relative_path = @path",
            [.. parameters]).ConfigureAwait(false);
    }

    /// <summary>Every library's name, keyed by id (for the Files list's <c>library_name</c> field).</summary>
    public static Task<Dictionary<long, string>> LibraryNamesAsync(UnitOfWork uow) =>
        uow.QueryAsync("SELECT id, name FROM libraries", reader => (Id: reader.GetInt64(0), Name: reader.GetString(1)))
           .ContinueWith(t => t.Result.ToDictionary(x => x.Id, x => x.Name), TaskScheduler.Default);

    private static ProcessingFileRecord Read(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        LibraryId = reader.GetInt64(1),
        RelativePath = SqliteValues.GetString(reader, 2),
        Status = SqliteValues.GetString(reader, 3),
        StatusReason = SqliteValues.GetString(reader, 4),
        BlockedByConnection = SqliteValues.GetStringOrNull(reader, 5),
        SizeBytes = SqliteValues.GetInt64(reader, 6),
        VideoWidth = reader.IsDBNull(7) ? null : reader.GetInt64(7),
        VideoHeight = reader.IsDBNull(8) ? null : reader.GetInt64(8),
        VideoCodec = SqliteValues.GetStringOrNull(reader, 9),
        AudioTrackCount = reader.IsDBNull(10) ? null : reader.GetInt64(10),
        SubtitleTrackCount = reader.IsDBNull(11) ? null : reader.GetInt64(11),
        DurationSeconds = reader.IsDBNull(12) ? null : reader.GetDouble(12),
        AudioCodecs = SqliteValues.GetStringOrNull(reader, 13),
        VideoBitDepth = reader.IsDBNull(14) ? null : reader.GetInt64(14),
        SizeChangedAt = SqliteValues.GetDateTimeOrNull(reader, 15),
        HoldUntil = SqliteValues.GetDateTimeOrNull(reader, 16),
        FailureClass = SqliteValues.GetStringOrNull(reader, 17),
        FailureAttempts = SqliteValues.GetInt64(reader, 18),
        NextRetryAt = SqliteValues.GetDateTimeOrNull(reader, 19),
        OutputCollisionPolicy = SqliteValues.GetStringOrNull(reader, 20),
        OutputCollisionAction = SqliteValues.GetStringOrNull(reader, 21),
        OutputCollisionReason = SqliteValues.GetStringOrNull(reader, 22),
        HardwareMethod = SqliteValues.GetStringOrNull(reader, 23),
        HardwareFellBackToSoftware = SqliteValues.GetBool(reader, 24),
        HardwareReason = SqliteValues.GetStringOrNull(reader, 25),
        LastSeenAt = SqliteValues.GetDateTimeOrNull(reader, 26),
        LastAttemptAt = SqliteValues.GetDateTimeOrNull(reader, 27),
        CreatedAt = SqliteValues.GetDateTime(reader, 28),
        UpdatedAt = SqliteValues.GetDateTime(reader, 29),
        ProcessedSourceSize = reader.IsDBNull(30) ? null : reader.GetInt64(30),
        ProcessedSourceMtimeNs = reader.IsDBNull(31) ? null : reader.GetInt64(31),
    };
}
