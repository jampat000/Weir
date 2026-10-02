using System.Text;
using Weir.Core.Logs;

namespace Weir.Infrastructure.SystemLog;

/// <summary>
/// <see cref="SystemLogRules"/> as SQL expressions, so a query can filter and count by level and category exactly as
/// the rules read them. Everything written into the SQL comes from those constant lists, never from a request.
/// </summary>
internal static class SystemLogSql
{
    /// <summary>
    /// A stored time as the fixed-shape text <c>2026-10-02 11:00:00.123456</c> (UTC). The columns hold more than one shape: with
    /// and without a UTC offset, and with no fraction at all when it is zero, as older releases wrote an exact second. Shaping
    /// them alike lets a time be compared and paged as plain text.
    /// </summary>
    public static string AtText(string column) =>
        $"CASE WHEN substr({column}, 20, 1) = '.' THEN substr({column}, 1, 26) ELSE substr({column}, 1, 19) || '.000000' END";

    /// <summary>The level of an Activity event, from its <c>result</c> column.</summary>
    public static string EventLevel(string resultColumn)
    {
        var builder = new StringBuilder($"CASE {resultColumn}");
        foreach (var (result, level) in SystemLogRules.EventResultLevels)
        {
            builder.Append(" WHEN ").Append(Literal(result)).Append(" THEN ").Append(Literal(level));
        }

        return builder.Append(" ELSE ").Append(Literal(SystemLogLevels.Info)).Append(" END").ToString();
    }

    /// <summary>The category of an Activity event, from its <c>event_type</c> column.</summary>
    public static string EventCategory(string eventTypeColumn)
    {
        var builder = new StringBuilder("CASE");
        foreach (var (prefix, category) in SystemLogRules.EventTypeCategories)
        {
            builder.Append(" WHEN substr(").Append(eventTypeColumn).Append(", 1, ").Append(prefix.Length).Append(") = ")
                .Append(Literal(prefix)).Append(" THEN ").Append(Literal(category));
        }

        return builder.Append(" ELSE ").Append(Literal(SystemLogCategories.Weir)).Append(" END").ToString();
    }

    /// <summary>The level of a job, from its <c>status</c> and <c>last_error</c> columns.</summary>
    public static string JobLevel(string statusColumn, string lastErrorColumn)
    {
        var builder = new StringBuilder("CASE");
        foreach (var (status, needsLastError, level) in SystemLogRules.JobLevels)
        {
            builder.Append(" WHEN ").Append(statusColumn).Append(" = ").Append(Literal(status));
            if (needsLastError)
            {
                builder.Append(" AND coalesce(").Append(lastErrorColumn).Append(", '') <> ''");
            }

            builder.Append(" THEN ").Append(Literal(level));
        }

        return builder.Append(" ELSE ").Append(Literal(SystemLogLevels.Info)).Append(" END").ToString();
    }

    /// <summary>The category of a job, from its <c>job_kind</c> column.</summary>
    public static string JobCategory(string jobKindColumn)
    {
        var builder = new StringBuilder("CASE ").Append(jobKindColumn);
        foreach (var rule in SystemLogRules.JobKinds)
        {
            builder.Append(" WHEN ").Append(Literal(rule.Kind)).Append(" THEN ").Append(Literal(rule.Category));
        }

        return builder.Append(" ELSE ").Append(Literal(SystemLogCategories.Weir)).Append(" END").ToString();
    }

    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
