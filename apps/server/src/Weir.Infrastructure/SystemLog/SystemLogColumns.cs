using Weir.Core.Logs;
using Weir.Core.Paging;
using Weir.Core.Time;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.SystemLog;

/// <summary>
/// The SQL that gives one source's rows each part of a log row's key (<see cref="SystemLogKeyPart"/>), so the source can list its
/// rows in a <see cref="SystemLogOrder"/> and find the ones after a cursor without reading the rest.
/// </summary>
/// <param name="Source">The source these expressions read.</param>
/// <param name="AtText">A row's time as the fixed-shape text <see cref="SystemLogSql.AtText"/> gives.</param>
/// <param name="IndexedAt">The stored time column an index covers, when the source has one: it lets a time-ordered page skip the rows far from the cursor.</param>
/// <param name="Id">The row's number in its source.</param>
/// <param name="Level">The row's level word.</param>
/// <param name="Category">The row's category word.</param>
/// <param name="WorkflowName">The name of the row's workflow, which is NULL when it has none or it no longer exists.</param>
internal sealed record SystemLogColumns(
    SystemLogSource Source,
    string AtText,
    string? IndexedAt,
    string Id,
    string Level,
    string Category,
    string WorkflowName)
{
    /// <summary>The length of a stored time up to its whole second: <c>2026-10-02 11:00:00</c>.</summary>
    private const int SecondsTextLength = 19;

    /// <summary>The key of each row in <paramref name="order"/>, one part for each of its <see cref="SystemLogOrder.Parts"/>.</summary>
    public IReadOnlyList<KeysetKey> Keys(SystemLogOrder order) =>
        [.. order.Parts.Select((part, index) => KeyOf(part, order.Shape[index]))];

    /// <summary>
    /// The <c>ORDER BY</c> clause for <paramref name="order"/>. The parts that are the same for every row of the source are left
    /// out, and a time is ordered by its stored column where an index covers it.
    /// </summary>
    public string OrderBy(SystemLogOrder order)
    {
        var keys = Keys(order);
        var ordered = new List<KeysetKey>();
        for (var index = 0; index < keys.Count; index++)
        {
            var part = order.Parts[index];
            if (!IsConstant(part))
            {
                ordered.Add(part == SystemLogKeyPart.At ? keys[index] with { Expression = IndexedAt ?? AtText } : keys[index]);
            }
        }

        return Keyset.OrderBy(ordered);
    }

    /// <summary>A cursor's values as the database compares them: a time is the text a stored time is read as.</summary>
    public IReadOnlyList<object?> SqlValues(SystemLogOrder order, IReadOnlyList<object?> key) =>
        [.. key.Select((value, index) => order.Parts[index] == SystemLogKeyPart.At ? AtTextOf((long)value!) : value)];

    /// <summary>
    /// A time-ordered read of a source with an indexed time column starts at the cursor's second, so the rows far from it are
    /// passed over without being read: stored times begin with the second they were written in.
    /// </summary>
    public (string Sql, (string Name, object? Value)[] Parameters)? IndexedBound(SystemLogOrder order, IReadOnlyList<object?> key)
    {
        if (order.Sort != SystemLogSort.Time || IndexedAt is null)
        {
            return null;
        }

        var cursorUtc = new DateTime((long)key[0]!, DateTimeKind.Utc);
        if (order.Direction == SortDirection.Ascending)
        {
            return ($"{IndexedAt} >= @cursor_lower", [("@cursor_lower", Timestamp.FromUtc(cursorUtc).ToSqlite()[..SecondsTextLength])]);
        }

        return ($"{IndexedAt} < @cursor_upper", [("@cursor_upper", Timestamp.FromUtc(cursorUtc.AddSeconds(1)).ToSqlite()[..SecondsTextLength])]);
    }

    private static string AtTextOf(long ticks) => Timestamp.FromUtc(new DateTime(ticks, DateTimeKind.Utc)).ToSqlite();

    private static bool IsConstant(SystemLogKeyPart part) => part is SystemLogKeyPart.SourceName or SystemLogKeyPart.Source;

    private KeysetKey KeyOf(SystemLogKeyPart part, KeysetPart shape) => part switch
    {
        SystemLogKeyPart.LevelRank => new(SystemLogSql.LevelRank(Level), shape),
        SystemLogKeyPart.SourceName => new(SystemLogSql.Literal(SystemLogSources.NameOf(Source)), shape),
        SystemLogKeyPart.CategoryName => new(Category, shape),
        SystemLogKeyPart.WorkflowMissing => new($"({WorkflowName} IS NULL)", shape),
        SystemLogKeyPart.WorkflowName => new($"{WorkflowName} COLLATE NOCASE", shape, CanBeNull: true),
        SystemLogKeyPart.At => new(AtText, shape),
        SystemLogKeyPart.Source => new(((int)Source).ToString(System.Globalization.CultureInfo.InvariantCulture), shape),
        _ => new(Id, shape),
    };
}
