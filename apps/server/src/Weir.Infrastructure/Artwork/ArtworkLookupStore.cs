using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Artwork;

/// <summary>The <c>artwork_lookups</c> and <c>artwork_posters</c> tables (migration 0035): one row per title and per stored image.</summary>
public sealed class ArtworkLookupStore
{
    /// <summary>Queue a title. A title already queued keeps its place, gains any hint it lacked, and is asked about again if it was missing and a manager has now named its poster.</summary>
    public Task EnqueueAsync(UnitOfWork uow, ArtworkLookupRequest request)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(request);
        return uow.ExecuteAsync(
            "INSERT INTO artwork_lookups (lookup_key, media_scope, title, year, tmdb_id, tvdb_id, imdb_id, poster_ref, priority) " +
            "VALUES ($key, $scope, $title, $year, $tmdb, $tvdb, $imdb, $ref, $priority) " +
            "ON CONFLICT (lookup_key) DO UPDATE SET " +
            "poster_ref = COALESCE(artwork_lookups.poster_ref, excluded.poster_ref), " +
            "tvdb_id = COALESCE(artwork_lookups.tvdb_id, excluded.tvdb_id), " +
            "imdb_id = COALESCE(artwork_lookups.imdb_id, excluded.imdb_id), " +
            "priority = MAX(artwork_lookups.priority, excluded.priority), " +
            "outcome = CASE WHEN artwork_lookups.outcome = 'missing' AND excluded.poster_ref IS NOT NULL THEN 'pending' ELSE artwork_lookups.outcome END, " +
            "retry_at = CASE WHEN artwork_lookups.outcome = 'missing' AND excluded.poster_ref IS NOT NULL THEN NULL ELSE artwork_lookups.retry_at END",
            ("$key", request.Key),
            ("$scope", request.MediaScope),
            ("$title", request.Title),
            ("$year", request.Year),
            ("$tmdb", request.TmdbId),
            ("$tvdb", request.TvdbId),
            ("$imdb", request.ImdbId),
            ("$ref", request.PosterRef),
            ("$priority", request.Priority));
    }

    /// <summary>The next title that is due: never asked about, or whose wait after a failure or a not-found has passed. Library files come last.</summary>
    public Task<ArtworkLookup?> NextDueAsync(UnitOfWork uow, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.QuerySingleAsync(
            "SELECT lookup_key, media_scope, title, year, tmdb_id, tvdb_id, poster_ref, attempts FROM artwork_lookups " +
            "WHERE outcome IN ('pending', 'missing') AND (retry_at IS NULL OR julianday(retry_at) <= julianday($now)) " +
            "ORDER BY priority DESC, created_at, rowid LIMIT 1",
            reader => new ArtworkLookup(
                SqliteValues.GetString(reader, 0),
                SqliteValues.GetString(reader, 1),
                SqliteValues.GetString(reader, 2),
                (int?)SqliteValues.GetInt64OrNull(reader, 3),
                SqliteValues.GetInt64OrNull(reader, 4),
                SqliteValues.GetInt64OrNull(reader, 5),
                SqliteValues.GetStringOrNull(reader, 6),
                (int)SqliteValues.GetInt64(reader, 7)),
            ("$now", TimestampColumns.Orm(now)));
    }

    public Task RecordFoundAsync(UnitOfWork uow, string key, string posterId)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.ExecuteAsync(
            "UPDATE artwork_lookups SET outcome = 'found', poster_id = $poster, attempts = 0, retry_at = NULL WHERE lookup_key = $key",
            ("$key", key),
            ("$poster", posterId));
    }

    public Task RecordMissingAsync(UnitOfWork uow, string key, DateTimeOffset retryAt)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.ExecuteAsync(
            "UPDATE artwork_lookups SET outcome = 'missing', poster_id = NULL, attempts = 0, retry_at = $retry WHERE lookup_key = $key",
            ("$key", key),
            ("$retry", TimestampColumns.Orm(retryAt)));
    }

    public Task RecordFailureAsync(UnitOfWork uow, string key, int attempts, DateTimeOffset retryAt)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.ExecuteAsync(
            "UPDATE artwork_lookups SET attempts = $attempts, retry_at = $retry WHERE lookup_key = $key",
            ("$key", key),
            ("$attempts", attempts),
            ("$retry", TimestampColumns.Orm(retryAt)));
    }

    public Task<StoredPoster?> FindPosterAsync(UnitOfWork uow, string posterId)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.QuerySingleAsync(
            "SELECT poster_id, content_type FROM artwork_posters WHERE poster_id = $id",
            reader => new StoredPoster(SqliteValues.GetString(reader, 0), SqliteValues.GetString(reader, 1)),
            ("$id", posterId));
    }

    /// <summary>Record a stored image. The same image stored twice is one row.</summary>
    public Task AddPosterAsync(UnitOfWork uow, string posterId, string sourceRef, string contentType, long sizeBytes)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.ExecuteAsync(
            "INSERT INTO artwork_posters (poster_id, source_ref, content_type, size_bytes) VALUES ($id, $ref, $type, $size) " +
            "ON CONFLICT (poster_id) DO NOTHING",
            ("$id", posterId),
            ("$ref", sourceRef),
            ("$type", contentType),
            ("$size", sizeBytes));
    }
}
