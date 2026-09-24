using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.LibraryMode;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.LibraryMode;

/// <summary>What a scan request or lookup answers.</summary>
public sealed record LibraryScanJobView(long JobId, string Status, string? LastError);

/// <summary>The most recently created scan row for a library, whatever its status.</summary>
public sealed record LibraryScanJobRow(long JobId, string Status, string? PayloadJson);

/// <summary>When the library's scan index was made and what that scan could not do, without the index itself.</summary>
public sealed record LibraryScanOutcome(DateTimeOffset GeneratedAt, IReadOnlyList<string> Errors);

/// <summary>
/// Requests a #505 library scan and reads back the latest one's result. Each request is an ordinary
/// <see cref="LibraryModeJobKinds.ScanKind"/> job row; the newest completed row for a library is the file index / plan cache
/// (see <c>docs/archive/server-port-notes.md</c>, "Library mode").
/// </summary>
public static class LibraryScanStore
{
    /// <summary>
    /// Enqueues the scan on <paramref name="uow"/>'s own connection and transaction (<see cref="ProcessingJobStore.EnqueueOrGet"/>),
    /// not <see cref="ProcessingJobStore.EnqueueOrGetAsync"/>: that opens its own connection, which — called while an API
    /// endpoint's write <see cref="UnitOfWork"/> is still open, as here — can deadlock against it, exactly the trap
    /// <see cref="Weir.Infrastructure.Processing.RequeueStore"/> already documents for the same reason.
    /// </summary>
    /// <remarks>
    /// <paramref name="scheduledAt"/> is the time a scheduled scan was due, recorded on the row so the next one is worked
    /// out from it (<see cref="LastScheduledRunAtAsync"/>); a scan someone asked for has none.
    /// </remarks>
    public static Task<ProcessingJob> RequestScanAsync(UnitOfWork uow, ProcessingJobStore jobs, long libraryId, string trigger, DateTimeOffset? scheduledAt = null)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(jobs);
        var payload = new PyDict().Set("library_id", libraryId).Set("trigger", trigger);
        if (scheduledAt is { } due)
        {
            payload.Set("scheduled_at", due.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }

        var job = jobs.EnqueueOrGet(
            uow.Connection,
            uow.WriteTransaction(),
            LibraryModeJobKinds.ScanDedupeKey(libraryId),
            LibraryModeJobKinds.ScanKind,
            PyJsonWriter.Dumps(payload, PyJsonFormat.Compact),
            JobQueueRules.DefaultMaxAttempts,
            0,
            LibraryModePriority.Low);
        return Task.FromResult(job);
    }

    /// <summary>
    /// Enqueues one <see cref="LibraryModeJobKinds.CleanKind"/> job, on <paramref name="uow"/>'s own connection and
    /// transaction for the same reason <see cref="RequestScanAsync"/> does. Public so the API layer (a different assembly)
    /// can request a clean without risking the same-connection deadlock.
    /// </summary>
    /// <remarks>
    /// <paramref name="manualPlan"/> is one person's own choice of tracks for this one file (#501 shape); the
    /// library's rules decide when it is null. <paramref name="expectedSizeBytes"/> is the size the file had when
    /// they chose, so a file that changed in between is refused rather than cleaned to a stale plan.
    /// </remarks>
    public static async Task<ProcessingJob> EnqueueCleanAsync(
        UnitOfWork uow,
        ProcessingJobStore jobs,
        long libraryId,
        string path,
        string trigger,
        bool confirmFinalRemoval,
        PyDict? manualPlan = null,
        long? expectedSizeBytes = null)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(jobs);
        var payload = new PyDict()
            .Set("library_id", libraryId)
            .Set("path", path)
            .Set("trigger", trigger)
            .Set("confirm_final_removal", confirmFinalRemoval);
        if (manualPlan is not null)
        {
            payload.Set("manual_plan", manualPlan);
            if (expectedSizeBytes is { } chosenAgainst)
            {
                payload.Set("expected_size_bytes", chosenAgainst);
            }
        }

        // One clean job per (library, path) keeps a second request from queueing the same work twice while the
        // first is still waiting or running. A *finished* row must not do that: it would mean a file could only ever
        // be cleaned once (until job rows are pruned days later), with the caller told it was queued. Clearing the
        // finished row first keeps the dedupe key meaning "one clean outstanding". Nothing reads a terminal clean
        // row — what happened to the file is in Activity.
        var dedupeKey = LibraryModeJobKinds.CleanDedupeKey(libraryId, path);
        var terminal = ProcessingJobStatus.Terminal;
        var names = terminal.Select((_, index) => $"@s{index}").ToArray();
        var parameters = terminal.Select((status, index) => ($"@s{index}", (object?)status))
            .Append(("@dedupe", dedupeKey))
            .ToArray();
        await uow.ExecuteAsync(
            $"DELETE FROM jobs WHERE dedupe_key = @dedupe AND status IN ({string.Join(", ", names)})",
            parameters).ConfigureAwait(false);

        return jobs.EnqueueOrGet(
            uow.Connection,
            uow.WriteTransaction(),
            dedupeKey,
            LibraryModeJobKinds.CleanKind,
            PyJsonWriter.Dumps(payload, PyJsonFormat.Compact),
            JobQueueRules.DefaultMaxAttempts,
            0,
            LibraryModePriority.Low);
    }

    /// <summary>Whether a scan for this library is already queued or running, so callers do not pile up duplicate requests.</summary>
    public static async Task<LibraryScanJobView?> ActiveScanAsync(UnitOfWork uow, long libraryId)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return await uow.QuerySingleAsync(
            "SELECT id, status, last_error FROM jobs WHERE job_kind = @kind AND dedupe_key LIKE @prefix ESCAPE '\\' " +
            "AND status IN ('pending', 'leased') ORDER BY id DESC LIMIT 1",
            reader => new LibraryScanJobView(reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)),
            ("@kind", LibraryModeJobKinds.ScanKind),
            ("@prefix", SqliteLike.Escape(LibraryModeJobKinds.ScanDedupeKeyPrefix(libraryId)) + "%")).ConfigureAwait(false);
    }

    /// <summary>
    /// When the library's most recent scheduled scan was due, whatever became of it, or null when it has never had one
    /// (or job-row retention has since pruned it, which only makes the next one due straight away).
    /// </summary>
    public static async Task<DateTimeOffset?> LastScheduledRunAtAsync(UnitOfWork uow, long libraryId)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var text = await uow.QuerySingleAsync(
            "SELECT json_extract(payload_json, '$.scheduled_at') FROM jobs WHERE job_kind = @kind AND dedupe_key LIKE @prefix ESCAPE '\\' " +
            "AND json_extract(payload_json, '$.trigger') = @trigger AND json_extract(payload_json, '$.scheduled_at') IS NOT NULL " +
            "ORDER BY id DESC LIMIT 1",
            reader => reader.GetString(0),
            ("@kind", LibraryModeJobKinds.ScanKind),
            ("@prefix", SqliteLike.Escape(LibraryModeJobKinds.ScanDedupeKeyPrefix(libraryId)) + "%"),
            ("@trigger", LibraryModeSchedule.Trigger)).ConfigureAwait(false);
        return DateTimeOffset.TryParse(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }

    /// <summary>The most recently created scan row for a library, whatever its status, for "what did the last scan say / do".</summary>
    public static async Task<LibraryScanJobRow?> LatestAsync(UnitOfWork uow, long libraryId)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return await uow.QuerySingleAsync(
            "SELECT id, status, payload_json FROM jobs WHERE job_kind = @kind AND dedupe_key LIKE @prefix ESCAPE '\\' " +
            "ORDER BY id DESC LIMIT 1",
            reader => new LibraryScanJobRow(reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)),
            ("@kind", LibraryModeJobKinds.ScanKind),
            ("@prefix", SqliteLike.Escape(LibraryModeJobKinds.ScanDedupeKeyPrefix(libraryId)) + "%")).ConfigureAwait(false);
    }

    /// <summary>
    /// When the library's file index was made and what that scan could not do, given its <paramref name="latest"/> scan
    /// row; null when nothing has ever been scanned. The job payload carries only <c>generated_at</c>/<c>errors</c> (the
    /// file list lives in <c>library_files</c>, #557). Job-row retention can prune the tracking job long after a scan
    /// completed, so rows in <c>library_files</c> are proof on their own that one ran. Only their existence is read: the
    /// Library screen asks for this on every refresh, and reading the index itself cost a full-table read (#709).
    /// </summary>
    public static async Task<LibraryScanOutcome?> OutcomeAsync(UnitOfWork uow, long libraryId, LibraryScanJobRow? latest, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(logger);
        if (latest is not { Status: ProcessingJobStatus.Completed })
        {
            return await HasFilesAsync(uow, libraryId).ConfigureAwait(false)
                ? new LibraryScanOutcome(DateTimeOffset.UnixEpoch, [])
                : null;
        }

        if (latest.PayloadJson is not { Length: > 0 } json)
        {
            return new LibraryScanOutcome(DateTimeOffset.UnixEpoch, []);
        }

        try
        {
            return PyJsonParser.Parse(json) is PyDict dict && LibraryScanSnapshot.FromPayload(dict, libraryId) is { } parsed
                ? new LibraryScanOutcome(parsed.GeneratedAt, parsed.Errors)
                : new LibraryScanOutcome(DateTimeOffset.UnixEpoch, []);
        }
        catch (PyJsonDecodeException exception)
        {
            // The file index still comes from library_files; only the scan time and its errors are lost.
            logger.LogWarning(exception, "Library scan job_id={JobId} has an unreadable payload; showing its files without the scan time or errors.", latest.JobId);
            return new LibraryScanOutcome(DateTimeOffset.UnixEpoch, []);
        }
    }

    /// <summary>
    /// The library's current file index. <c>library_files</c> always holds whatever the previous scan recorded (a scan in
    /// progress has not written its own new rows yet, #557), so this is simply the table's current contents for the
    /// library: what the next scan's ffprobe cache is seeded from, and what a whole-library confirmation counts.
    /// </summary>
    public static async Task<IReadOnlyList<LibraryScanFileEntry>> CurrentFilesAsync(UnitOfWork uow, long libraryId) =>
        await FilesForLibraryAsync(uow, libraryId).ConfigureAwait(false);

    /// <summary>The index entries for <paramref name="paths"/>, keyed by path; a path the index does not hold is absent.</summary>
    public static async Task<IReadOnlyDictionary<string, LibraryScanFileEntry>> FilesAtPathsAsync(UnitOfWork uow, long libraryId, IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(paths);
        var wanted = new PyList(paths.Distinct(StringComparer.Ordinal).Select(path => (PyJson)new PyStr(path)));
        var rows = await uow.QueryAsync(
            $"SELECT {FileColumns} FROM {FilesWithProbes} WHERE f.library_id = @id AND f.path IN (SELECT value FROM json_each(@paths))",
            ReadFile,
            ("@id", libraryId),
            ("@paths", PyJsonWriter.Dumps(wanted, PyJsonFormat.Compact))).ConfigureAwait(false);
        return rows.ToDictionary(file => file.Path, StringComparer.Ordinal);
    }

    private static async Task<bool> HasFilesAsync(UnitOfWork uow, long libraryId) =>
        await uow.CountAsync("SELECT EXISTS (SELECT 1 FROM library_files WHERE library_id = @id)", ("@id", libraryId)).ConfigureAwait(false) != 0;

    /// <summary>
    /// Records the job's own small outcome (<c>ok</c>/<c>reason</c>/<c>generated_at</c>/<c>errors</c>) on its payload,
    /// keeping every other key. The file list itself goes to <c>library_files</c> through
    /// <see cref="LibraryFileIndexWriter"/> (#557: kept out of the job payload, so job-row retention cannot delete it).
    /// </summary>
    public static async Task RecordResultAsync(UnitOfWork uow, long jobId, LibraryScanOutcome outcome, bool ok, string? reason)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(outcome);
        var existingJson = await uow.ScalarAsync("SELECT payload_json FROM jobs WHERE id = @id", ("@id", jobId)).ConfigureAwait(false);
        PyDict payload;
        try
        {
            payload = existingJson is string text && text.Length > 0 && PyJsonParser.Parse(text) is PyDict dict ? dict : new PyDict();
        }
        catch (PyJsonDecodeException)
        {
            payload = new PyDict();
        }

        payload.Set("ok", ok);
        if (reason is not null)
        {
            payload.Set("reason", reason);
        }

        payload.Set(LibraryScanSnapshot.PayloadKey, new PyDict()
            .Set("generated_at", outcome.GeneratedAt.ToUnixTimeSeconds())
            .Set("errors", new PyList(outcome.Errors.Select(e => (PyJson)new PyStr(e)))));
        await uow.ExecuteAsync(
            "UPDATE jobs SET payload_json = @payload, updated_at = CURRENT_TIMESTAMP WHERE id = @id",
            ("@payload", PyJsonWriter.Dumps(payload, PyJsonFormat.Compact)),
            ("@id", jobId)).ConfigureAwait(false);
    }

    /// <summary>The index columns <see cref="ReadFile"/> reads, in its order, from <c>library_files</c> as <c>f</c> and its probe document as <c>p</c>.</summary>
    private const string FileColumns =
        "f.path, f.size_bytes, f.mtime, f.classification, f.summary, f.reason, f.removed_audio_tracks, f.removed_subtitle_tracks, " +
        "f.manager_kind, f.manager_title, p.probe_json, f.estimated_bytes_saved, f.manager_connection_id, f.manager_title_id, " +
        "f.manager_file_id, f.manager_quality_profile_id, f.problem_kind, f.link_count";

    private const string FilesWithProbes = "library_files AS f LEFT JOIN library_file_probes AS p ON p.library_file_id = f.id";

    private static async Task<List<LibraryScanFileEntry>> FilesForLibraryAsync(UnitOfWork uow, long libraryId) =>
        await uow.QueryAsync(
            $"SELECT {FileColumns} FROM {FilesWithProbes} WHERE f.library_id = @id ORDER BY f.path",
            ReadFile,
            ("@id", libraryId)).ConfigureAwait(false);

    private static LibraryScanFileEntry ReadFile(SqliteDataReader reader) => new(
        Path: SqliteValues.GetString(reader, 0),
        SizeBytes: SqliteValues.GetInt64(reader, 1),
        ModifiedTimeUnixSeconds: SqliteValues.GetInt64(reader, 2),
        Classification: ClassificationOf(SqliteValues.GetString(reader, 3)),
        Summary: SqliteValues.GetStringOrNull(reader, 4),
        Reason: SqliteValues.GetStringOrNull(reader, 5),
        RemovedAudioCount: (int)SqliteValues.GetInt64(reader, 6),
        RemovedSubtitleCount: (int)SqliteValues.GetInt64(reader, 7),
        ManagerKind: SqliteValues.GetStringOrNull(reader, 8),
        ManagerTitle: SqliteValues.GetStringOrNull(reader, 9),
        ProbeJson: SqliteValues.GetStringOrNull(reader, 10),
        EstimatedBytesSaved: SqliteValues.GetInt64(reader, 11),
        ManagerConnectionId: reader.IsDBNull(12) ? null : reader.GetInt64(12),
        ManagerTitleId: SqliteValues.GetStringOrNull(reader, 13),
        ManagerFileId: reader.IsDBNull(14) ? null : reader.GetInt64(14),
        ManagerQualityProfileId: reader.IsDBNull(15) ? null : reader.GetInt64(15),
        ProblemKind: LibraryProblems.Parse(SqliteValues.GetStringOrNull(reader, 16)),
        LinkCount: reader.IsDBNull(17) ? null : (int)reader.GetInt64(17));

    private static LibraryFileClassification ClassificationOf(string value) => value switch
    {
        "matches" => LibraryFileClassification.Matches,
        "would_change" => LibraryFileClassification.WouldChange,
        _ => LibraryFileClassification.CannotProcess,
    };
}
