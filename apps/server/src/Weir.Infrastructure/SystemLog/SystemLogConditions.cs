using Weir.Core.Logs;
using Weir.Core.Time;

namespace Weir.Infrastructure.SystemLog;

/// <summary>One read of the log, as the sources receive it.</summary>
/// <param name="Filter">What to show.</param>
/// <param name="After">Where the page starts: the rows after this position, or from the newest when null.</param>
/// <param name="Take">The most rows a source returns: a page, plus one to tell whether another follows.</param>
internal sealed record SystemLogRequest(SystemLogFilter Filter, SystemLogPosition? After, int Take);

/// <summary>A SQL WHERE clause built up from conditions and the parameters they name.</summary>
internal sealed class SystemLogConditions
{
    /// <summary>The length of a stored time up to its whole second: <c>2026-10-02 11:00:00</c>.</summary>
    private const int SecondsTextLength = 19;

    private readonly List<string> _conditions = [];
    private readonly List<(string Name, object? Value)> _parameters = [];

    public SystemLogConditions(IEnumerable<string>? conditions = null, IEnumerable<(string Name, object? Value)>? parameters = null)
    {
        _conditions.AddRange(conditions ?? []);
        _parameters.AddRange(parameters ?? []);
    }

    public (string Name, object? Value)[] Parameters => [.. _parameters];

    public string WhereText => _conditions.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", _conditions);

    public SystemLogConditions Add(string condition, params (string Name, object? Value)[] parameters)
    {
        _conditions.Add(condition);
        _parameters.AddRange(parameters);
        return this;
    }

    /// <summary><paramref name="expression"/> is one of <paramref name="values"/>; nothing is added when there are none.</summary>
    public SystemLogConditions AddIn<T>(string expression, string name, IReadOnlyList<T> values)
        where T : notnull
    {
        if (values.Count == 0)
        {
            return this;
        }

        var names = values.Select((_, index) => $"@{name}{index}").ToList();
        return Add($"{expression} IN ({string.Join(", ", names)})", [.. names.Zip(values, (parameter, value) => (parameter, (object?)value))]);
    }

    /// <summary>A copy that can take more conditions without changing this one.</summary>
    public SystemLogConditions Copy() => new(_conditions, _parameters);

    /// <summary>
    /// Keeps rows that come after <paramref name="cursor"/> in the newest-first order, for a source whose rows carry their
    /// time as the fixed-shape text <paramref name="atText"/> (see <see cref="SystemLogSql.AtText"/>) and their number in
    /// <paramref name="keyColumn"/>. Rows of the same instant fall after the cursor when their source sorts after the
    /// cursor's, or, from the same source, when their number is lower. A column an index covers can be named as
    /// <paramref name="indexedColumn"/>, so the rows far from the cursor are passed over without being read.
    /// </summary>
    public SystemLogConditions AddAfter(string atText, string keyColumn, SystemLogSource source, SystemLogPosition? cursor, string? indexedColumn = null)
    {
        if (cursor is not { } position)
        {
            return this;
        }

        var cursorUtc = position.At.UtcDateTime;
        var at = ("@cursor_at", (object?)Timestamp.FromUtc(cursorUtc).ToSqlite());
        if (indexedColumn is not null)
        {
            // Stored times begin with the second they were written in, so nothing past the cursor's second can be after it.
            var upper = Timestamp.FromUtc(cursorUtc.AddSeconds(1)).ToSqlite()[..SecondsTextLength];
            Add($"{indexedColumn} < @cursor_upper", ("@cursor_upper", upper));
        }

        if (source == position.Source)
        {
            return Add($"({atText} < @cursor_at OR ({atText} = @cursor_at AND {keyColumn} < @cursor_key))", at, ("@cursor_key", position.Key));
        }

        return source < position.Source
            ? Add($"{atText} <= @cursor_at", at)
            : Add($"{atText} < @cursor_at", at);
    }
}
