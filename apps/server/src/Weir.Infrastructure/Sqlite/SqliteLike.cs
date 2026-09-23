namespace Weir.Infrastructure.Sqlite;

/// <summary>Helpers for SQLite <c>LIKE</c> patterns.</summary>
public static class SqliteLike
{
    /// <summary>
    /// Escapes <c>\</c>, <c>%</c> and <c>_</c> with a backslash so the text matches literally. The query must say
    /// <c>ESCAPE '\'</c>.
    /// </summary>
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }
}
