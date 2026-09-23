using System.Globalization;
using Weir.Core.Time;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// Writes and reads the two text shapes the database's timestamp columns hold. Rows written by earlier
/// releases use these shapes, so they must keep parsing and comparing correctly.
/// </summary>
/// <remarks>
/// The offset shape, <c>2026-04-10 13:00:00.123456+00:00</c> with the fraction omitted when zero, is used
/// for the claim's <c>@now</c> and <c>@lease_exp</c>. The offset-less shape, <c>2026-04-10 12:00:30.123456</c>
/// with microseconds always present, is used for <c>not_before</c>, prune cutoffs and
/// <c>processing_paused_until</c>. Both are read as UTC. Compare them in SQL with <c>julianday()</c>, not as
/// text, because the two shapes do not sort correctly against each other as strings (#540).
/// </remarks>
public static class PythonTimestamps
{
    /// <summary>The offset shape, fraction omitted when zero: <c>2026-04-10 13:00:00.123456+00:00</c>.</summary>
    public static string Adapter(DateTimeOffset value) =>
        PyDateTime.FromDateTimeOffset(value.ToUniversalTime()).IsoFormat(' ');

    /// <summary>The offset-less shape, microseconds always present: <c>2026-04-10 12:00:30.123456</c>.</summary>
    public static string Orm(DateTimeOffset value) => PyDateTime.FromDateTimeOffset(value.ToUniversalTime()).ToSqlite();

    /// <summary>Read any of the stored shapes (including <c>CURRENT_TIMESTAMP</c>); a value without an offset is UTC.</summary>
    public static DateTimeOffset? Parse(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)!.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var styles = DateTimeStyles.AllowWhiteSpaces;
        var hasOffset = text.Length > 19 && (text.EndsWith('Z') || text.LastIndexOfAny(['+', '-']) > 10);
        if (hasOffset)
        {
            return DateTimeOffset.Parse(text.Replace(' ', 'T'), CultureInfo.InvariantCulture, styles);
        }

        var parsed = DateTime.Parse(text.Replace(' ', 'T'), CultureInfo.InvariantCulture, styles | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        return new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc));
    }
}
