using Weir.Core.Paging;

namespace Weir.Infrastructure.Sqlite;

/// <summary>One part of a sort key as the database sees it.</summary>
/// <param name="Expression">The SQL that gives the part's value for a row.</param>
/// <param name="Part">What its values are and which way it runs.</param>
/// <param name="CanBeNull">Whether the expression can give NULL, which sorts before every value.</param>
internal readonly record struct KeysetKey(string Expression, KeysetPart Part, bool CanBeNull = false)
{
    public string Direction => Part.Direction == SortDirection.Descending ? "DESC" : "ASC";
}

/// <summary>The SQL for a list sorted by a key and paged by position: its <c>ORDER BY</c>, and the rows that come after a given key.</summary>
internal static class Keyset
{
    /// <summary>The <c>ORDER BY</c> clause (with its leading space) that lists rows in the order of <paramref name="keys"/>.</summary>
    public static string OrderBy(IReadOnlyList<KeysetKey> keys) =>
        " ORDER BY " + string.Join(", ", keys.Select(key => $"{key.Expression} {key.Direction}"));

    /// <summary>
    /// A condition that holds for the rows after the row whose values are <paramref name="after"/>, one value for each of
    /// <paramref name="keys"/>: a row is after it when the first part where they differ goes the list's way. Values are
    /// passed as parameters named <c>@{parameterPrefix}0</c>, <c>@{parameterPrefix}1</c> and so on.
    /// </summary>
    public static (string Sql, (string Name, object? Value)[] Parameters) After(
        IReadOnlyList<KeysetKey> keys,
        IReadOnlyList<object?> after,
        string parameterPrefix)
    {
        var parameters = new List<(string Name, object? Value)>();
        var alternatives = new List<string>();
        for (var index = 0; index < keys.Count; index++)
        {
            var terms = new List<string>();
            for (var earlier = 0; earlier < index; earlier++)
            {
                terms.Add(Equal(keys[earlier], after[earlier], ParameterName(parameterPrefix, earlier)));
            }

            terms.Add(Beyond(keys[index], after[index], ParameterName(parameterPrefix, index)));
            alternatives.Add("(" + string.Join(" AND ", terms) + ")");
            if (after[index] is not null)
            {
                parameters.Add((ParameterName(parameterPrefix, index), after[index]));
            }
        }

        return ("(" + string.Join(" OR ", alternatives) + ")", [.. parameters]);
    }

    private static string ParameterName(string prefix, int index) => $"@{prefix}{index}";

    private static string Equal(KeysetKey key, object? value, string parameter) =>
        value is null ? $"{key.Expression} IS NULL" : $"{key.Expression} = {parameter}";

    private static string Beyond(KeysetKey key, object? value, string parameter)
    {
        if (key.Part.Direction == SortDirection.Ascending)
        {
            return value is null ? $"{key.Expression} IS NOT NULL" : $"{key.Expression} > {parameter}";
        }

        if (value is null)
        {
            return "0";
        }

        return key.CanBeNull ? $"({key.Expression} < {parameter} OR {key.Expression} IS NULL)" : $"{key.Expression} < {parameter}";
    }
}
