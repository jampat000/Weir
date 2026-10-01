using Microsoft.Data.Sqlite;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Artwork;

/// <summary>The <c>artwork_files</c> table (migration 0035): which title each file belongs to, and so which poster it shows.</summary>
public sealed class ArtworkFileStore
{
    /// <summary>How many paths one query names, well under SQLite's limit on bound values.</summary>
    private const int PathsPerQuery = 400;

    /// <summary>Record a file's title unless the file already has one.</summary>
    public Task LinkAsync(UnitOfWork uow, long libraryId, string path, string? lookupKey)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.ExecuteAsync(
            "INSERT INTO artwork_files (library_id, relative_path, lookup_key) VALUES ($library, $path, $key) ON CONFLICT (library_id, relative_path) DO NOTHING",
            ("$library", libraryId),
            ("$path", path),
            ("$key", lookupKey));
    }

    /// <summary>Record the title a media manager gave a file, replacing what was read from its name.</summary>
    public Task RelinkAsync(UnitOfWork uow, long libraryId, string path, string? lookupKey, int? season, int? episode)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.ExecuteAsync(
            "INSERT INTO artwork_files (library_id, relative_path, lookup_key, season, episode) VALUES ($library, $path, $key, $season, $episode) " +
            "ON CONFLICT (library_id, relative_path) DO UPDATE SET lookup_key = excluded.lookup_key, season = excluded.season, episode = excluded.episode",
            ("$library", libraryId),
            ("$path", path),
            ("$key", lookupKey),
            ("$season", season),
            ("$episode", episode));
    }

    /// <summary>Processing files with no title yet, newest first.</summary>
    public Task<List<ArtworkCandidate>> UnlinkedProcessingFilesAsync(UnitOfWork uow, int limit)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.QueryAsync(
            "SELECT f.library_id, f.relative_path, l.media_type, NULL FROM files f JOIN libraries l ON l.id = f.library_id " +
            "LEFT JOIN artwork_files a ON a.library_id = f.library_id AND a.relative_path = f.relative_path " +
            "WHERE a.library_id IS NULL ORDER BY f.id DESC LIMIT $limit",
            ReadCandidate,
            ("$limit", limit));
    }

    /// <summary>Library-scan files with no title yet, newest first, with the title their manager gave them when it matched one.</summary>
    public Task<List<ArtworkCandidate>> UnlinkedLibraryFilesAsync(UnitOfWork uow, int limit)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.QueryAsync(
            "SELECT f.library_id, f.path, l.media_type, f.manager_title FROM library_files f JOIN libraries l ON l.id = f.library_id " +
            "LEFT JOIN artwork_files a ON a.library_id = f.library_id AND a.relative_path = f.path " +
            "WHERE a.library_id IS NULL ORDER BY f.id DESC LIMIT $limit",
            ReadCandidate,
            ("$limit", limit));
    }

    /// <summary>The poster id of every given file whose title has a poster. A file with none is left out.</summary>
    public async Task<Dictionary<(long LibraryId, string Path), string>> PosterIdsAsync(UnitOfWork uow, IEnumerable<(long LibraryId, string Path)> files)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(files);
        var found = new Dictionary<(long, string), string>();
        foreach (var library in files.GroupBy(file => file.LibraryId))
        {
            foreach (var paths in library.Select(file => file.Path).Distinct(StringComparer.Ordinal).Chunk(PathsPerQuery))
            {
                var names = paths.Select((_, index) => $"$p{index}").ToList();
                var parameters = paths.Select((path, index) => ($"$p{index}", (object?)path)).Append(("$library", (object?)library.Key));
                var rows = await uow.QueryAsync(
                    "SELECT a.relative_path, l.poster_id FROM artwork_files a JOIN artwork_lookups l ON l.lookup_key = a.lookup_key " +
                    $"WHERE l.outcome = '{ArtworkOutcomes.Found}' AND a.library_id = $library AND a.relative_path IN ({string.Join(", ", names)})",
                    reader => (Path: SqliteValues.GetString(reader, 0), PosterId: SqliteValues.GetString(reader, 1)),
                    [.. parameters]).ConfigureAwait(false);
                foreach (var (path, posterId) in rows)
                {
                    found[(library.Key, path)] = posterId;
                }
            }
        }

        return found;
    }

    private static ArtworkCandidate ReadCandidate(SqliteDataReader reader) => new(
        SqliteValues.GetInt64(reader, 0),
        SqliteValues.GetString(reader, 1),
        SqliteValues.GetString(reader, 2),
        SqliteValues.GetStringOrNull(reader, 3));
}
