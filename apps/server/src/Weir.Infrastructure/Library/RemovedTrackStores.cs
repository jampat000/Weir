using Microsoft.Data.Sqlite;
using Weir.Core.Library;
using Weir.Core.Rules;
using Weir.Core.Time;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Library;

/// <summary>
/// The durable <see cref="IRemovedTrackStore"/> (#509, #557): the <c>removed_tracks</c> table from migration
/// <c>0006_removed_tracks.sql</c>, one row per removed track. A dedicated table rather than <c>file_logs</c>, because
/// <see cref="Processing.FileLogStore.PruneAsync"/> deletes any <c>file_logs</c> row past its
/// window regardless of outcome, which would drop records from the "titles missing tracks your new rules
/// keep" list.
/// </summary>
public sealed class FileLogRemovedTrackStore : IRemovedTrackStore
{
    private readonly SqliteDatabase _database;
    private readonly TimeProvider _time;

    public FileLogRemovedTrackStore(SqliteDatabase database, TimeProvider time)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public async Task RecordAsync(RemovedTrackFileKey key, IReadOnlyList<RemovedTrackRecord> tracks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(tracks);
        await using var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        await uow.ExecuteAsync(
            "DELETE FROM removed_tracks WHERE relative_path = @relative_path AND library_id IS @library_id",
            ("@relative_path", key.RelativePath), ("@library_id", key.LibraryId)).ConfigureAwait(false);

        var recordedAt = SqliteValues.ToSqlite(PyDateTime.FromUtc(_time.GetUtcNow().UtcDateTime));
        foreach (var track in tracks)
        {
            await uow.ExecuteAsync(
                "INSERT INTO removed_tracks (library_id, relative_path, language, track_type, codec, variant, reason, recorded_at) " +
                "VALUES (@library_id, @relative_path, @language, @track_type, @codec, @variant, @reason, @recorded_at)",
                ("@library_id", key.LibraryId),
                ("@relative_path", key.RelativePath),
                ("@language", track.Language),
                ("@track_type", track.Type == RemovedTrackType.Subtitle ? "subtitle" : "audio"),
                ("@codec", track.Codec),
                ("@variant", track.Variant),
                ("@reason", track.Reason),
                ("@recorded_at", recordedAt)).ConfigureAwait(false);
        }

        await uow.CommitAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RemovedTrackRecord>> GetAsync(RemovedTrackFileKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        await using var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        var rows = await uow.QueryAsync(
            "SELECT language, track_type, codec, variant, reason FROM removed_tracks " +
            "WHERE relative_path = @relative_path AND library_id IS @library_id ORDER BY id",
            reader => ReadFrom(reader, offset: 0),
            ("@relative_path", key.RelativePath), ("@library_id", key.LibraryId)).ConfigureAwait(false);
        return rows;
    }

    public async Task<IReadOnlyDictionary<RemovedTrackFileKey, IReadOnlyList<RemovedTrackRecord>>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        var rows = await uow.QueryAsync(
            "SELECT library_id, relative_path, language, track_type, codec, variant, reason FROM removed_tracks ORDER BY library_id, relative_path, id",
            reader => (
                LibraryId: reader.IsDBNull(0) ? (long?)null : reader.GetInt64(0),
                RelativePath: SqliteValues.GetString(reader, 1),
                Track: ReadFrom(reader, offset: 2)),
            []).ConfigureAwait(false);

        var result = new Dictionary<RemovedTrackFileKey, List<RemovedTrackRecord>>();
        foreach (var row in rows)
        {
            var key = new RemovedTrackFileKey(row.LibraryId, row.RelativePath);
            if (!result.TryGetValue(key, out var list))
            {
                result[key] = list = [];
            }

            list.Add(row.Track);
        }

        return result.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<RemovedTrackRecord>)pair.Value);
    }

    private static RemovedTrackRecord ReadFrom(SqliteDataReader reader, int offset) => new()
    {
        Language = SqliteValues.GetString(reader, offset),
        Type = SqliteValues.GetString(reader, offset + 1) == "subtitle" ? RemovedTrackType.Subtitle : RemovedTrackType.Audio,
        Codec = SqliteValues.GetString(reader, offset + 2),
        Variant = SqliteValues.GetStringOrNull(reader, offset + 3),
        Reason = SqliteValues.GetString(reader, offset + 4),
    };
}
