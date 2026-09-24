using Microsoft.Data.Sqlite;
using Weir.Core.Processing;
using Weir.Core.Time;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.LibraryMode;

/// <summary>Which library cleans History asks for: the same filters its download list takes.</summary>
public sealed record LibraryCleanHistoryFilter
{
    /// <summary>The same page as the download list: 200 unless asked, never more than 1,000.</summary>
    public const int DefaultLimit = 200;
    public const int MaxLimit = 1000;

    public long? LibraryId { get; init; }
    public string? PathContains { get; init; }
    public Timestamp? Since { get; init; }
    public int Limit { get; init; } = DefaultLimit;

    public int ClampedLimit => Math.Clamp(Limit, 1, MaxLimit);
}

/// <summary>The latest thing a library clean did to one file.</summary>
public sealed record LibraryCleanHistoryRow(
    long Id,
    Timestamp RecordedAt,
    string Outcome,
    string Detail,
    string? Trigger,
    long? LibraryId,
    string RelativePath);

/// <summary>
/// Library cleans for History (#695). What a clean did to a file lives only in the Activity event it wrote (a finished
/// clean job row is deleted when the file is cleaned again), so History reads those events: the newest one per file,
/// the way the download list keeps one row per file.
/// </summary>
public sealed class LibraryCleanHistoryStore
{
    public Task<List<LibraryCleanHistoryRow>> ListAsync(UnitOfWork uow, LibraryCleanHistoryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(filter);
        var eventTypes = LibraryCleanOutcomes.ByEventType.Keys.ToArray();
        var typeNames = eventTypes.Select((_, index) => $"@event_type_{index}").ToArray();
        var parameters = eventTypes.Select((type, index) => ($"@event_type_{index}", (object?)type)).ToList();
        var clauses = new List<string>
        {
            "activity_events.id IN (SELECT max(id) FROM activity_events " +
            $"WHERE event_type IN ({string.Join(", ", typeNames)}) AND relative_path IS NOT NULL AND relative_path <> '' " +
            "GROUP BY library_id, relative_path)",
        };
        if (filter.LibraryId is { } libraryId)
        {
            clauses.Add("activity_events.library_id = @library_id");
            parameters.Add(("@library_id", libraryId));
        }

        if (!string.IsNullOrEmpty(filter.PathContains))
        {
            clauses.Add("activity_events.relative_path LIKE @path_contains ESCAPE '\\'");
            parameters.Add(("@path_contains", "%" + SqliteLike.Escape(filter.PathContains) + "%"));
        }

        if (filter.Since is { } since)
        {
            clauses.Add("julianday(activity_events.created_at) >= julianday(@since)");
            parameters.Add(("@since", SqliteValues.ToSqlite(since)));
        }

        parameters.Add(("@limit", filter.ClampedLimit));
        return uow.QueryAsync(
            "SELECT activity_events.id, activity_events.created_at, activity_events.event_type, activity_events.title, " +
            "activity_events.\"trigger\", activity_events.library_id, activity_events.relative_path FROM activity_events " +
            $"WHERE {string.Join(" AND ", clauses)} " +
            "ORDER BY activity_events.created_at DESC, activity_events.id DESC LIMIT @limit",
            Read,
            [.. parameters]);
    }

    private static LibraryCleanHistoryRow Read(SqliteDataReader reader) => new(
        SqliteValues.GetInt64(reader, 0),
        SqliteValues.GetDateTime(reader, 1),
        LibraryCleanOutcomes.ByEventType[SqliteValues.GetString(reader, 2)],
        SqliteValues.GetString(reader, 3),
        SqliteValues.GetStringOrNull(reader, 4),
        reader.IsDBNull(5) ? null : SqliteValues.GetInt64(reader, 5),
        SqliteValues.GetString(reader, 6));
}
