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

/// <summary>
/// Requests a #505 library scan and reads back the latest one's result. Each request is an ordinary
/// <see cref="LibraryModeJobKinds.ScanKind"/> job row; the newest completed row for a library is the file index / plan cache
/// (see <c>apps/server/README.md</c>, "Library mode").
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
    /// The latest completed scan's snapshot (the file index / plan cache), or null when nothing has ever
    /// been scanned. The job payload carries only <c>generated_at</c>/<c>errors</c> (the file list lives in
    /// <c>library_files</c>, #557), so those two come from the latest completed job's payload when it still
    /// exists, but <c>library_files</c> itself is read unconditionally: job-row retention can prune the
    /// tracking job long after a scan completed, and that must not lose the file index the scan produced.
    /// </summary>
    public static async Task<LibraryScanSnapshot?> LatestSnapshotAsync(UnitOfWork uow, long libraryId, ILogger logger)
    {
        var files = await FilesForLibraryAsync(uow, libraryId).ConfigureAwait(false);
        var latest = await LatestAsync(uow, libraryId).ConfigureAwait(false);
        if (latest is not { Status: ProcessingJobStatus.Completed })
        {
            // No completed job survives to say a scan ever ran. If library_files still has rows for this
            // library (its own tracking job was pruned), that is itself proof one did; otherwise, nothing
            // has been scanned yet.
            return files.Count == 0 ? null : new LibraryScanSnapshot(libraryId, DateTimeOffset.UnixEpoch, files, []);
        }

        var generatedAt = DateTimeOffset.UnixEpoch;
        IReadOnlyList<string> errors = [];
        if (latest.PayloadJson is { Length: > 0 } json)
        {
            try
            {
                if (PyJsonParser.Parse(json) is PyDict dict && LibraryScanSnapshot.FromPayload(dict, libraryId) is { } parsed)
                {
                    generatedAt = parsed.GeneratedAt;
                    errors = parsed.Errors;
                }
            }
            catch (PyJsonDecodeException exception)
            {
                // The file index still comes from library_files; only the scan time and its errors are lost.
                logger.LogWarning(exception, "Library scan job_id={JobId} has an unreadable payload; showing its files without the scan time or errors.", latest.JobId);
            }
        }

        return new LibraryScanSnapshot(libraryId, generatedAt, files, errors);
    }

    /// <summary>
    /// The library's current file index, which seeds the next scan's ffprobe cache. <c>library_files</c>
    /// always holds whatever the previous scan recorded (a scan in progress has not written its own new rows
    /// yet, #557), so this is simply the table's current contents for the library.
    /// </summary>
    public static async Task<IReadOnlyList<LibraryScanFileEntry>> PreviousFilesForCacheAsync(UnitOfWork uow, long libraryId) =>
        await FilesForLibraryAsync(uow, libraryId).ConfigureAwait(false);

    /// <summary>
    /// Records the job's own small outcome (<c>ok</c>/<c>reason</c>/<c>generated_at</c>/<c>errors</c>) on its
    /// payload, keeping every other key, and replaces the library's <c>library_files</c> rows with
    /// <paramref name="snapshot"/>'s file list (#557: the file list is kept out of the job payload, so job-row
    /// retention cannot delete it).
    /// </summary>
    public static async Task RecordResultAsync(UnitOfWork uow, long jobId, LibraryScanSnapshot snapshot, bool ok, string? reason)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(snapshot);
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
            .Set("generated_at", snapshot.GeneratedAt.ToUnixTimeSeconds())
            .Set("errors", new PyList(snapshot.Errors.Select(e => (PyJson)new PyStr(e)))));
        await uow.ExecuteAsync(
            "UPDATE jobs SET payload_json = @payload, updated_at = CURRENT_TIMESTAMP WHERE id = @id",
            ("@payload", PyJsonWriter.Dumps(payload, PyJsonFormat.Compact)),
            ("@id", jobId)).ConfigureAwait(false);

        await ReplaceFilesAsync(uow, snapshot.LibraryId, snapshot.Files).ConfigureAwait(false);
    }

    private static async Task<List<LibraryScanFileEntry>> FilesForLibraryAsync(UnitOfWork uow, long libraryId) =>
        await uow.QueryAsync(
            "SELECT path, size_bytes, mtime, classification, summary, reason, removed_audio_tracks, removed_subtitle_tracks, " +
            "manager_kind, manager_title, probe_json, estimated_bytes_saved, manager_connection_id, manager_title_id, " +
            "manager_file_id, manager_quality_profile_id, problem_kind, link_count FROM library_files WHERE library_id = @id ORDER BY path",
            ReadFile,
            ("@id", libraryId)).ConfigureAwait(false);

    /// <summary>
    /// Replaces a library's file index. Each row also gets the #568 codec/resolution/track columns and its
    /// <c>library_file_facets</c> rows, derived here from the ffprobe JSON the scan already cached on the row
    /// (<see cref="LibraryFileFactsReader"/>) — so the Library view's totals, breakdowns and facet filters are
    /// plain indexed SQL and never re-open a probe document, let alone re-probe a file.
    /// </summary>
    private static async Task ReplaceFilesAsync(UnitOfWork uow, long libraryId, IReadOnlyList<LibraryScanFileEntry> files)
    {
        // library_file_facets cascades from library_files, so deleting the rows clears their facets too.
        await uow.ExecuteAsync("DELETE FROM library_files WHERE library_id = @id", ("@id", libraryId)).ConfigureAwait(false);
        foreach (var file in files)
        {
            var facts = LibraryFileFactsReader.Derive(file.ProbeJson);
            var fileId = await uow.ExecuteScalarWriteAsync(
                "INSERT INTO library_files (library_id, path, size_bytes, mtime, classification, summary, reason, " +
                "removed_audio_tracks, removed_subtitle_tracks, estimated_bytes_saved, manager_kind, manager_title, " +
                "manager_connection_id, manager_title_id, manager_file_id, manager_quality_profile_id, probe_json, " +
                "video_codec, video_height, resolution_class, audio_track_count, subtitle_track_count, audio_summary, " +
                "subtitle_summary, link_count, problem_kind) VALUES " +
                "(@library_id, @path, @size_bytes, @mtime, @classification, @summary, @reason, @removed_audio, @removed_subtitle, " +
                "@estimated_bytes_saved, @manager_kind, @manager_title, @manager_connection_id, @manager_title_id, @manager_file_id, " +
                "@manager_quality_profile_id, @probe_json, @video_codec, @video_height, @resolution_class, @audio_tracks, " +
                "@subtitle_tracks, @audio_summary, @subtitle_summary, @link_count, @problem_kind) RETURNING id",
                ("@library_id", libraryId),
                ("@path", file.Path),
                ("@size_bytes", file.SizeBytes),
                ("@mtime", file.ModifiedTimeUnixSeconds),
                ("@classification", LibraryScanFileEntry.ClassificationName(file.Classification)),
                ("@summary", file.Summary),
                ("@reason", file.Reason),
                ("@removed_audio", file.RemovedAudioCount),
                ("@removed_subtitle", file.RemovedSubtitleCount),
                ("@estimated_bytes_saved", file.EstimatedBytesSaved),
                ("@manager_kind", file.ManagerKind),
                ("@manager_title", file.ManagerTitle),
                ("@manager_connection_id", file.ManagerConnectionId),
                ("@manager_title_id", file.ManagerTitleId),
                ("@manager_file_id", file.ManagerFileId),
                ("@manager_quality_profile_id", file.ManagerQualityProfileId),
                ("@probe_json", file.ProbeJson),
                ("@video_codec", facts.VideoCodec),
                ("@video_height", facts.VideoHeight),
                ("@resolution_class", facts.ResolutionClass),
                ("@audio_tracks", facts.AudioTrackCount),
                ("@subtitle_tracks", facts.SubtitleTrackCount),
                ("@audio_summary", facts.AudioSummary),
                ("@subtitle_summary", facts.SubtitleSummary),
                ("@link_count", file.LinkCount),
                ("@problem_kind", file.ProblemKind is { } kind ? LibraryProblems.Name(kind) : null)).ConfigureAwait(false);

            if (fileId is null)
            {
                continue;
            }

            foreach (var facet in facts.Facets)
            {
                await uow.ExecuteAsync(
                    "INSERT OR IGNORE INTO library_file_facets (library_id, library_file_id, facet, value) " +
                    "VALUES (@library_id, @file_id, @facet, @value)",
                    ("@library_id", libraryId),
                    ("@file_id", Convert.ToInt64(fileId, System.Globalization.CultureInfo.InvariantCulture)),
                    ("@facet", facet.Facet),
                    ("@value", facet.Value)).ConfigureAwait(false);
            }
        }
    }

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
