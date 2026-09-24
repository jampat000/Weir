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

    /// <summary>Changes a row only when one of its columns differs, so an unchanged row costs a comparison, not a write.</summary>
    private static readonly string UpdateSql =
        "UPDATE library_files SET " + string.Join(", ", StoredColumnNames.Select(column => $"{column} = @{column}")) +
        ", scanned_at = CURRENT_TIMESTAMP WHERE id = @id AND NOT (" +
        string.Join(" AND ", StoredColumnNames.Select(column => $"{column} IS @{column}")) + ")";

    /// <summary>Makes the library's index hold exactly <paramref name="files"/>.</summary>
    public static async Task ReplaceAsync(SqliteDatabase database, long libraryId, IReadOnlyList<LibraryScanFileEntry> files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(files);
        var existing = await ExistingIdsAsync(database, libraryId, cancellationToken).ConfigureAwait(false);
        var kept = files.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        var gone = existing.Where(pair => !kept.Contains(pair.Key)).Select(pair => pair.Value).ToList();
        foreach (var chunk in gone.Chunk(ChunkSize))
        {
            await WriteLockTurns.TakeAsync(() => DeleteAsync(database, chunk, cancellationToken), cancellationToken).ConfigureAwait(false);
        }

        foreach (var chunk in files.Chunk(ChunkSize))
        {
            await WriteLockTurns.TakeAsync(() => WriteChunkAsync(database, libraryId, chunk, existing, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteChunkAsync(
        SqliteDatabase database, long libraryId, LibraryScanFileEntry[] chunk, Dictionary<string, long> existing, CancellationToken cancellationToken)
    {
        var uow = await UnitOfWork.OpenAsync(database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            foreach (var file in chunk)
            {
                await WriteAsync(uow, libraryId, file, existing.TryGetValue(file.Path, out var id) ? id : null).ConfigureAwait(false);
            }

            await uow.CommitAsync().ConfigureAwait(false);
        }
    }

    private static async Task<Dictionary<string, long>> ExistingIdsAsync(SqliteDatabase database, long libraryId, CancellationToken cancellationToken)
    {
        var uow = await UnitOfWork.OpenAsync(database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            var rows = await uow.QueryAsync(
                "SELECT path, id FROM library_files WHERE library_id = @id",
                reader => (Path: reader.GetString(0), Id: reader.GetInt64(1)),
                ("@id", libraryId)).ConfigureAwait(false);
            return rows.ToDictionary(row => row.Path, row => row.Id, StringComparer.Ordinal);
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
                ("@ids", PyJsonWriter.Dumps(new PyList(ids.Select(id => (PyJson)new PyInt(id))), PyJsonFormat.Compact))).ConfigureAwait(false);
            await uow.CommitAsync().ConfigureAwait(false);
        }
    }

    private static async Task WriteAsync(UnitOfWork uow, long libraryId, LibraryScanFileEntry file, long? existingId)
    {
        var facts = LibraryFileFactsReader.Derive(file.ProbeJson);
        var values = RowValues(file, facts);
        long fileId;
        bool rowChanged;
        if (existingId is not { } rowId)
        {
            fileId = Convert.ToInt64(
                await uow.ExecuteScalarWriteAsync(InsertSql, [("@library_id", libraryId), ("@path", file.Path), .. values]).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            rowChanged = true;
        }
        else
        {
            fileId = rowId;
            rowChanged = await uow.ExecuteAsync(UpdateSql, [("@id", rowId), .. values]).ConfigureAwait(false) > 0;
        }

        var probeChanged = await WriteProbeAsync(uow, fileId, file.ProbeJson).ConfigureAwait(false);
        if (rowChanged || probeChanged)
        {
            await WriteFacetsAsync(uow, libraryId, fileId, facts).ConfigureAwait(false);
        }
    }

    /// <summary>Stores the file's probe document when it differs from the one on record; returns whether it did.</summary>
    private static async Task<bool> WriteProbeAsync(UnitOfWork uow, long fileId, string? probeJson)
    {
        if (probeJson is null)
        {
            return await uow.ExecuteAsync("DELETE FROM library_file_probes WHERE library_file_id = @id", ("@id", fileId)).ConfigureAwait(false) > 0;
        }

        return await uow.ExecuteAsync(
            "INSERT INTO library_file_probes (library_file_id, probe_json) VALUES (@id, @probe) " +
            "ON CONFLICT (library_file_id) DO UPDATE SET probe_json = excluded.probe_json WHERE library_file_probes.probe_json IS NOT excluded.probe_json",
            ("@id", fileId),
            ("@probe", probeJson)).ConfigureAwait(false) > 0;
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
