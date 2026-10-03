using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Jobs;

/// <summary>Rows for the <c>jobs</c> table, written while the server is stopped (no API creates a job in an arbitrary state).</summary>
internal static class JobRows
{
    public const string WorkTempStaleSweepKind = "processing.work_temp_stale_sweep.v1";

    /// <summary>One <c>jobs</c> row; extra columns (<c>attempt_count</c>, <c>last_error</c>, ...) pass through.</summary>
    public static int Insert(
        SqliteConnection connection,
        string dedupeKey,
        string jobKind,
        string status = "pending",
        DateTime? updatedAt = null,
        params (string Column, object? Value)[] columns)
    {
        var stamp = SeedSql.UtcText(updatedAt ?? DateTime.UtcNow);
        var values = new List<(string Column, object? Value)>
        {
            ("dedupe_key", dedupeKey),
            ("job_kind", jobKind),
            ("status", status),
            ("created_at", stamp),
            ("updated_at", stamp),
        };
        values.AddRange(columns);
        var names = string.Join(", ", values.Select(value => value.Column));
        var marks = string.Join(", ", values.Select(value => "$" + value.Column));
        var id = SeedSql.InsertAndGetId(
            connection,
            $"INSERT INTO jobs ({names}) VALUES ({marks})",
            [.. values.Select(value => ("$" + value.Column, value.Value))]);
        return (int)id;
    }
}
