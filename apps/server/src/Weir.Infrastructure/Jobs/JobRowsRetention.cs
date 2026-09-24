using System.Globalization;
using Weir.Core.Jobs;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Jobs;

/// <summary>What one retention tick removed.</summary>
public sealed record JobRowsPruneCounts(int Processing, int HandoffLedger, int Activity)
{
    public int Total => Processing + Activity;
}

/// <summary>
/// Periodic pruning of terminal job rows: terminal <c>jobs</c> rows past <c>WEIR_JOB_ROWS_RETENTION_DAYS</c>, terminal hand-off ledger
/// rows past 90 days, and Activity past the suite's <c>activity_retention_days</c>, each a batch per transaction
/// (<see cref="BatchedDeletes"/>).
/// </summary>
public sealed class JobRowsRetention
{
    /// <summary>How long terminal hand-off ledger rows are kept: hand-off answers outlive job rows on purpose (#480).</summary>
    public const int LedgerRetentionDays = 90;

    /// <summary>The Activity retention used when the suite settings row does not exist yet.</summary>
    public const int DefaultActivityRetentionDays = 90;

    /// <summary>The ledger's terminal states.</summary>
    public static readonly IReadOnlyList<string> LedgerTerminalStates = ["cancelled", "completed", "failed", "passed-through", "rejected"];

    private readonly ProcessingJobStore _queue;

    public JobRowsRetention(ProcessingJobStore queue) => _queue = queue ?? throw new ArgumentNullException(nameof(queue));

    /// <summary>One retention tick: jobs, hand-off ledger and Activity.</summary>
    public async Task<JobRowsPruneCounts> RunTickAsync(int jobRowsRetentionDays, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var database = _queue.Database;
        var processing = await PruneJobRowsAsync(database, now - TimeSpan.FromDays(jobRowsRetentionDays), cancellationToken).ConfigureAwait(false);
        var ledger = await PruneLedgerAsync(database, now, cancellationToken).ConfigureAwait(false);
        var activity = await PruneActivityAsync(database, await ActivityRetentionDaysAsync(database, cancellationToken).ConfigureAwait(false), now, cancellationToken)
            .ConfigureAwait(false);
        return new JobRowsPruneCounts(processing, ledger, activity);
    }

    /// <summary>Delete terminal rows whose <c>updated_at</c> is older than <paramref name="cutoff"/>.</summary>
    private static Task<int> PruneJobRowsAsync(SqliteDatabase database, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var (names, parameters) = InList("s", ProcessingJobStatus.Terminal);
        return BatchedDeletes.DeleteAsync(
            database, "jobs", $"status IN ({names}) AND updated_at < @cutoff", [.. parameters, ("@cutoff", TimestampColumns.Orm(cutoff))], cancellationToken);
    }

    private static Task<int> PruneLedgerAsync(SqliteDatabase database, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (names, parameters) = InList("state", LedgerTerminalStates);
        return BatchedDeletes.DeleteAsync(
            database,
            "media_manager_handoffs",
            $"state IN ({names}) AND last_changed_at < @cutoff",
            [.. parameters, ("@cutoff", TimestampColumns.Orm(now - TimeSpan.FromDays(LedgerRetentionDays)))],
            cancellationToken);
    }

    /// <summary>Delete Activity older than <paramref name="retentionDays"/>; zero or less keeps everything.</summary>
    private static Task<int> PruneActivityAsync(SqliteDatabase database, int retentionDays, DateTimeOffset now, CancellationToken cancellationToken) =>
        retentionDays <= 0
            ? Task.FromResult(0)
            : BatchedDeletes.DeleteAsync(
                database, "activity_events", "created_at < @cutoff", [("@cutoff", TimestampColumns.Orm(now - TimeSpan.FromDays(retentionDays)))], cancellationToken);

    private static async Task<int> ActivityRetentionDaysAsync(SqliteDatabase database, CancellationToken cancellationToken)
    {
        var uow = await UnitOfWork.OpenAsync(database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            var value = await uow.ScalarAsync("SELECT activity_retention_days FROM suite_settings WHERE id = 1").ConfigureAwait(false);
            return value is null or DBNull ? DefaultActivityRetentionDays : (int)Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Named parameters for an <c>IN (…)</c> list: <c>@{prefix}0, @{prefix}1, …</c> and their values.</summary>
    private static (string Names, (string Name, object? Value)[] Parameters) InList(string prefix, IReadOnlyList<string> values) =>
        (string.Join(", ", values.Select((_, index) => $"@{prefix}{index}")),
         [.. values.Select((value, index) => ($"@{prefix}{index}", (object?)value))]);
}
