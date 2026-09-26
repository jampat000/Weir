using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>One file a person chose to keep without processing again (#785), by the exact bytes Weir saw it at.</summary>
public sealed record FileSkipMarker(long SizeBytes, long MtimeNs);

/// <summary>
/// SQLite access for <c>file_skip_markers</c>: a watched-folder scan reads every marker for a library once
/// (<see cref="ForLibraryAsync"/>) and skips a candidate whose current size and modification time still match its
/// marker. A person choosing "Keep" writes one (<see cref="SetAsync"/>); choosing "delete" or "retry" instead clears
/// it (<see cref="ClearAsync"/>), so a later "Keep" of a changed file is not shadowed by a stale row.
/// </summary>
public sealed class FileSkipMarkerStore
{
    public Task<Dictionary<string, FileSkipMarker>> ForLibraryAsync(UnitOfWork uow, long libraryId) =>
        uow.QueryAsync(
                "SELECT relative_path, size_bytes, mtime_ns FROM file_skip_markers WHERE library_id = @lib",
                reader => (Path: reader.GetString(0), Marker: new FileSkipMarker(reader.GetInt64(1), reader.GetInt64(2))),
                ("@lib", libraryId))
            .ContinueWith(t => t.Result.ToDictionary(row => row.Path, row => row.Marker, StringComparer.Ordinal), TaskScheduler.Default);

    public Task SetAsync(UnitOfWork uow, long libraryId, string relativePath, long sizeBytes, long mtimeNs) =>
        uow.ExecuteAsync(
            "INSERT INTO file_skip_markers (library_id, relative_path, size_bytes, mtime_ns) VALUES (@lib, @path, @size, @mtime) " +
            "ON CONFLICT (library_id, relative_path) DO UPDATE SET size_bytes = @size, mtime_ns = @mtime, created_at = CURRENT_TIMESTAMP",
            ("@lib", libraryId), ("@path", relativePath), ("@size", sizeBytes), ("@mtime", mtimeNs));

    public Task ClearAsync(UnitOfWork uow, long libraryId, string relativePath) =>
        uow.ExecuteAsync(
            "DELETE FROM file_skip_markers WHERE library_id = @lib AND relative_path = @path",
            ("@lib", libraryId), ("@path", relativePath));
}
