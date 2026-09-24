using Microsoft.Data.Sqlite;
using Weir.Core.Jobs;

namespace Weir.Infrastructure.Jobs;

/// <summary>Reads: a single job, every job, and the rows behind them.</summary>
public sealed partial class ProcessingJobStore
{
    /// <summary>Every jobs column, in the order <see cref="ReadJob"/> reads them.</summary>
    internal const string JobColumns =
        "id, dedupe_key, job_kind, payload_json, status, lease_owner, lease_expires_at, attempt_count, " +
        "max_attempts, last_error, not_before, runner_cost, priority, created_at, updated_at";

    public Task<ProcessingJob?> GetAsync(long jobId, CancellationToken cancellationToken = default) =>
        ReadAsync((connection, transaction) => Get(connection, transaction, jobId), cancellationToken);

    public Task<IReadOnlyList<ProcessingJob>> ListAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<ProcessingJob>>(
            (connection, transaction) => Query(connection, transaction, $"SELECT {JobColumns} FROM jobs ORDER BY id"),
            cancellationToken);

    internal static ProcessingJob? Get(SqliteConnection connection, SqliteTransaction transaction, long jobId) =>
        Query(connection, transaction, $"SELECT {JobColumns} FROM jobs WHERE id = @id", ("@id", jobId)).FirstOrDefault();

    /// <summary>One jobs row selected as <see cref="JobColumns"/>, in that column order.</summary>
    internal static ProcessingJob ReadJob(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        TimestampColumns.Parse(reader.GetValue(6)),
        (int)reader.GetInt64(7),
        (int)reader.GetInt64(8),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        TimestampColumns.Parse(reader.GetValue(10)),
        (int)reader.GetInt64(11),
        (int)reader.GetInt64(12),
        TimestampColumns.Parse(reader.GetValue(13)) ?? DateTimeOffset.MinValue,
        TimestampColumns.Parse(reader.GetValue(14)) ?? DateTimeOffset.MinValue);

    internal static List<ProcessingJob> Query(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, transaction, sql, parameters);
        using var reader = command.ExecuteReader();
        var rows = new List<ProcessingJob>();
        while (reader.Read())
        {
            rows.Add(ReadJob(reader));
        }

        return rows;
    }

    /// <summary>Every pending or leased job of <paramref name="jobKind"/>, oldest first.</summary>
    internal static List<ProcessingJob> ActiveOfKind(SqliteConnection connection, SqliteTransaction transaction, string jobKind) =>
        Query(
            connection,
            transaction,
            $"SELECT {JobColumns} FROM jobs WHERE job_kind = @kind AND status IN (@pending, @leased) ORDER BY id",
            ("@kind", jobKind),
            ("@pending", ProcessingJobStatus.Pending),
            ("@leased", ProcessingJobStatus.Leased));

    internal static ProcessingJob? GetByDedupeKey(SqliteConnection connection, SqliteTransaction transaction, string dedupeKey) =>
        Query(connection, transaction, $"SELECT {JobColumns} FROM jobs WHERE dedupe_key = @dedupe", ("@dedupe", dedupeKey)).FirstOrDefault();
}
