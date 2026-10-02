using Weir.Core.Logs;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.SystemLog;

/// <summary>One read of the log, as the sources receive it.</summary>
/// <param name="Filter">What to show.</param>
/// <param name="Order">What the rows are ordered by, and which way.</param>
/// <param name="After">Where the page starts: the rows after the one with this key (see <see cref="SystemLogOrder.KeyOf"/>), or from the first when null.</param>
/// <param name="Take">The most rows a source returns: a page, plus one to tell whether another follows.</param>
internal sealed record SystemLogRequest(SystemLogFilter Filter, SystemLogOrder Order, IReadOnlyList<object?>? After, int Take);

/// <summary>A SQL WHERE clause built up from conditions and the parameters they name.</summary>
internal sealed class SystemLogConditions
{
    private const string AfterParameterPrefix = "after_";

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

    /// <summary>Keeps rows that come after the row whose key is <paramref name="request"/>'s cursor in its order, for a source whose rows <paramref name="columns"/> describes.</summary>
    public SystemLogConditions AddAfter(SystemLogColumns columns, SystemLogRequest request)
    {
        if (request.After is not { } after)
        {
            return this;
        }

        var sqlKey = columns.SqlValues(request.Order, after);
        if (columns.IndexedBound(request.Order, after) is { } bound)
        {
            Add(bound.Sql, bound.Parameters);
        }

        var (sql, parameters) = Keyset.After(columns.Keys(request.Order), sqlKey, AfterParameterPrefix);
        return Add(sql, parameters);
    }
}
