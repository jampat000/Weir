using System.Globalization;
using Weir.Core.Json;
using Weir.Core.LibraryMode;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.LibraryMode;

/// <summary>
/// Brings a library's file index (<c>library_files</c>, its probe documents and its facet rows) in line with what a scan
/// found, touching only rows that differ, a chunk of files per short transaction (#715).
/// </summary>
/// <remarks>
/// A whole-library delete and rewrite held the write lock for seconds on a large library and grew the WAL by hundreds of
/// megabytes for rows that had not changed. Each row keeps its id while it is unchanged, so its facets and probe stay put.
/// Readers can see a mix of the old and new index while the chunks land; the scan job completes only after the last one.
/// Between chunks the writer stands back so other lanes get the lock (<see cref="WriteLockTurns"/>).
/// </remarks>
public static class LibraryFileIndexWriter
{
    /// <summary>Files written per transaction: short enough that no other writer waits long for the lock.</summary>
    internal const int ChunkSize = 500;

    private const string StoredColumns =
        "size_bytes, mtime, classification, summary, reason, removed_audio_tracks, removed_subtitle_tracks, estimated_bytes_saved, " +
        "manager_kind, manager_title, manager_connection_id, manager_title_id, manager_file_id, manager_quality_profile_id, " +
        "video_codec, video_height, resolution_class, audio_track_count, subtitle_track_count, audio_summary, subtitle_summary, " +
        "link_count, problem_kind";

    private static readonly string[] StoredColumnNames = StoredColumns.Split(", ");

    private static readonly string InsertSql =
        $"INSERT INTO library_files (library_id, path, {StoredColumns}) VALUES (@library_id, @path, " +
        string.Join(", ", StoredColumnNames.Select(column => "@" + column)) + ") RETURNING id";

    /// <summary>
    /// Changes a row only when one of its columns differs from the scan's new values, and only while every stored column
    /// still holds what the walk read for it: a preflight or another lane that set one of them (<c>problem_kind</c>,
    /// recorded by <see cref="LibraryViewStore.RecordPreflightProblemAsync"/>, is the one known writer today) since then
    /// wins, the same way <see cref="Weir.Infrastructure.Processing.FileStateStore.RecordScannedStateAsync"/> and the
    /// vanished-file sweep condition their writes on the status they read. A row that no longer matches is left for the
    /// next scan.
    /// </summary>
    private static readonly string UpdateSql =
        "UPDATE library_files SET " + string.Join(", ", StoredColumnNames.Select(column => $"{column} = @{column}")) +
        ", scanned_at = CURRENT_TIMESTAMP WHERE id = @id AND NOT (" +
        string.Join(" AND ", StoredColumnNames.Select(column => $"{column} IS @{column}")) + ") AND " +
        string.Join(" AND ", StoredColumnNames.Select(column => $"{column} IS @old_{column}"));

    /// <summary>One row as the walk read it, before deciding what the scan would write.</summary>
    internal sealed record ExistingLibraryFile(long Id, IReadOnlyList<object?> StoredValues);

    /// <summary>Makes the library's index hold exactly <paramref name="files"/>.</summary>
    public static async Task ReplaceAsync(SqliteDatabase database, long libraryId, IReadOnlyList<LibraryScanFileEntry> files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(files);
        var existing = await ExistingRowsAsync(database, libraryId, cancellationToken).ConfigureAwait(false);
        var kept = files.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        var gone = existing.Where(pair => !kept.Contains(pair.Key)).Select(pair => pair.Value.Id).ToList();
        foreach (var chunk in gone.Chunk(ChunkSize))
        {
            await WriteLockTurns.TakeAsync(() => DeleteAsync(database, chunk, cancellationToken), cancellationToken).ConfigureAwait(false);
        }

        foreach (var chunk in files.Chunk(ChunkSize))
        {
            await WriteLockTurns.TakeAsync(() => WriteChunkAsync(database, libraryId, chunk, existing, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes one chunk against a snapshot of what a scan's walk found. Exposed for <see cref="ReplaceAsync"/> and for
    /// tests that need to hold a snapshot across a write elsewhere, to exercise the race the conditional write in
    /// <see cref="UpdateSql"/> guards against.
    /// </summary>
    internal static async Task WriteChunkAsync(
        SqliteDatabase database, long libraryId, LibraryScanFileEntry[] chunk, Dictionary<string, ExistingLibraryFile> existing, CancellationToken cancellationToken)
    {
        var uow = await UnitOfWork.OpenAsync(database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            foreach (var file in chunk)
            {
                await WriteAsync(uow, libraryId, file, existing.GetValueOrDefault(file.Path)).ConfigureAwait(false);
            }

            await uow.CommitAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Every row a scan's walk would compare against, as of now. Exposed for tests; see <see cref="WriteChunkAsync"/>.</summary>
    internal static async Task<Dictionary<string, ExistingLibraryFile>> ExistingRowsAsync(SqliteDatabase database, long libraryId, CancellationToken cancellationToken)
    {
        var uow = await UnitOfWork.OpenAsync(database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            var rows = await uow.QueryAsync(
                $"SELECT path, id, {StoredColumns} FROM library_files WHERE library_id = @id",
                reader =>
                {
                    var values = new object?[StoredColumnNames.Length];
                    for (var index = 0; index < values.Length; index++)
                    {
                        values[index] = reader.IsDBNull(index + 2) ? null : reader.GetValue(index + 2);
                    }

                    return (Path: reader.GetString(0), Row: new ExistingLibraryFile(reader.GetInt64(1), values));
                },
                ("@id", libraryId)).ConfigureAwait(false);
            return rows.ToDictionary(row => row.Path, row => row.Row, StringComparer.Ordinal);
        }
    }

    /// <summary>Probe documents and facet rows cascade from <c>library_files</c>.</summary>
    private static async Task DeleteAsync(SqliteDatabase database, long[] ids, CancellationToken cancellationToken)
    {
        var uow = await UnitOfWork.OpenAsync(database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            await uow.ExecuteAsync(
                "DELETE FROM library_files WHERE id IN (SELECT value FROM json_each(@ids))",
                ("@ids", WireJsonWriter.Dumps(new WireArray(ids.Select(id => (WireValue)new WireInteger(id))), WireJsonFormat.Compact))).ConfigureAwait(false);
            await uow.CommitAsync().ConfigureAwait(false);
        }
    }

    private static async Task WriteAsync(UnitOfWork uow, long libraryId, LibraryScanFileEntry file, ExistingLibraryFile? existing)
    {
        var facts = LibraryFileFactsReader.Derive(file.ProbeJson);
        var values = RowValues(file, facts);
        long fileId;
        if (existing is null)
        {
            fileId = Convert.ToInt64(
                await uow.ExecuteScalarWriteAsync(InsertSql, [("@library_id", libraryId), ("@path", file.Path), .. values]).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }
        else
        {
            var oldValues = StoredColumnNames.Zip(existing.StoredValues, (column, value) => ($"@old_{column}", value));
            var applied = await uow.ExecuteAsync(UpdateSql, [("@id", existing.Id), .. values, .. oldValues]).ConfigureAwait(false) > 0;
            if (!applied)
            {
                // Nothing differs from what the walk found, or a column it read (problem_kind, today) changed since:
                // the row and its probe/facets are left alone either way, and a genuinely stale row is caught next scan.
                return;
            }

            fileId = existing.Id;
        }

        await WriteProbeAsync(uow, fileId, file.ProbeJson).ConfigureAwait(false);
        await WriteFacetsAsync(uow, libraryId, fileId, facts).ConfigureAwait(false);
    }

    /// <summary>Stores the file's probe document, only writing when it differs from the one on record.</summary>
    private static async Task WriteProbeAsync(UnitOfWork uow, long fileId, string? probeJson)
    {
        if (probeJson is null)
        {
            await uow.ExecuteAsync("DELETE FROM library_file_probes WHERE library_file_id = @id", ("@id", fileId)).ConfigureAwait(false);
            return;
        }

        await uow.ExecuteAsync(
            "INSERT INTO library_file_probes (library_file_id, probe_json) VALUES (@id, @probe) " +
            "ON CONFLICT (library_file_id) DO UPDATE SET probe_json = excluded.probe_json WHERE library_file_probes.probe_json IS NOT excluded.probe_json",
            ("@id", fileId),
            ("@probe", probeJson)).ConfigureAwait(false);
    }

    private static async Task WriteFacetsAsync(UnitOfWork uow, long libraryId, long fileId, LibraryFileFacts facts)
    {
        await uow.ExecuteAsync("DELETE FROM library_file_facets WHERE library_file_id = @id", ("@id", fileId)).ConfigureAwait(false);
        foreach (var facet in facts.Facets)
        {
            await uow.ExecuteAsync(
                "INSERT OR IGNORE INTO library_file_facets (library_id, library_file_id, facet, value) VALUES (@library_id, @file_id, @facet, @value)",
                ("@library_id", libraryId),
                ("@file_id", fileId),
                ("@facet", facet.Facet),
                ("@value", facet.Value)).ConfigureAwait(false);
        }
    }

    /// <summary>The stored columns' values, named as in <see cref="StoredColumns"/>.</summary>
    private static (string Name, object? Value)[] RowValues(LibraryScanFileEntry file, LibraryFileFacts facts) =>
    [
        ("@size_bytes", file.SizeBytes),
        ("@mtime", file.ModifiedTimeUnixSeconds),
        ("@classification", LibraryScanFileEntry.ClassificationName(file.Classification)),
        ("@summary", file.Summary),
        ("@reason", file.Reason),
        ("@removed_audio_tracks", file.RemovedAudioCount),
        ("@removed_subtitle_tracks", file.RemovedSubtitleCount),
        ("@estimated_bytes_saved", file.EstimatedBytesSaved),
        ("@manager_kind", file.ManagerKind),
        ("@manager_title", file.ManagerTitle),
        ("@manager_connection_id", file.ManagerConnectionId),
        ("@manager_title_id", file.ManagerTitleId),
        ("@manager_file_id", file.ManagerFileId),
        ("@manager_quality_profile_id", file.ManagerQualityProfileId),
        ("@video_codec", facts.VideoCodec),
        ("@video_height", facts.VideoHeight),
        ("@resolution_class", facts.ResolutionClass),
        ("@audio_track_count", facts.AudioTrackCount),
        ("@subtitle_track_count", facts.SubtitleTrackCount),
        ("@audio_summary", facts.AudioSummary),
        ("@subtitle_summary", facts.SubtitleSummary),
        ("@link_count", file.LinkCount),
        ("@problem_kind", file.ProblemKind is { } kind ? LibraryProblems.Name(kind) : null),
    ];
}
