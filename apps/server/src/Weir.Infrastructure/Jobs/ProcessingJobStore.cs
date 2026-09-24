using System.Globalization;
using Microsoft.Data.Sqlite;
using Weir.Core.Jobs;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// The durable <c>jobs</c> queue: enqueue-or-get by dedupe key, atomic claim with a lease, complete,
/// fail with retry and backoff, and the operator actions.
/// </summary>
/// <remarks>
/// Every operation runs in one <c>BEGIN IMMEDIATE</c> transaction, so SQLite's single writer serialises
/// it against every other connection. The claim is a single
/// <c>UPDATE … WHERE id = (SELECT … LIMIT 1) RETURNING id</c> statement that compares timestamps with
/// <c>julianday()</c> rather than as text (#540 item 2). As text, <c>+</c> (an offset marker) sorts before
/// <c>.</c> (a fraction's first character), so a <c>not_before</c> with microseconds would compare greater
/// than an <c>@now</c> at the same instant whose zero fraction is omitted, and a retried job would miss its
/// own <c>not_before</c> second. Julian day reads both stored shapes (see <see cref="TimestampColumns"/>)
/// identically.
/// </remarks>
/// <remarks>
/// Split across partial files by concern: this file holds construction, transactions and the shared SQL
/// plumbing; <c>ProcessingJobStore.Enqueue.cs</c>, <c>.Claim.cs</c>, <c>.Completion.cs</c>,
/// <c>.OperatorActions.cs</c> and <c>.Reads.cs</c> hold the operations themselves.
/// </remarks>
public sealed partial class ProcessingJobStore
{
    public const string MetricsModule = "processing";

    private readonly SqliteDatabase _database;
    private readonly TimeProvider _time;
    private readonly IJobQueueMetrics _metrics;
    private readonly WorkerWakeSignals? _wakeSignals;

    public ProcessingJobStore(SqliteDatabase database, TimeProvider time, IJobQueueMetrics? metrics = null, WorkerWakeSignals? wakeSignals = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _metrics = metrics ?? NoJobQueueMetrics.Instance;
        _wakeSignals = wakeSignals;
    }

    public SqliteDatabase Database => _database;

    /// <summary>
    /// Run <paramref name="work"/> in one <c>BEGIN IMMEDIATE</c> transaction and commit. The queue's own
    /// synchronous helpers (<see cref="Execute"/>, <see cref="Scalar"/> and the static methods in the other
    /// partial files) take a raw connection and transaction rather than the async <see cref="UnitOfWork"/>
    /// API, so this hands them the ones <see cref="UnitOfWork"/> itself opened and committed: one
    /// transaction mechanism server-wide, with this as the queue's calling convention on top of it.
    /// </summary>
    public async Task<T> InTransactionAsync<T>(Func<SqliteConnection, SqliteTransaction, T> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            var transaction = uow.WriteTransaction();
            var result = work(uow.Connection, transaction);
            await uow.CommitAsync().ConfigureAwait(false);
            Activity.ActivityNotifications.TransactionCommitted(_database, transaction);
            return result;
        }
    }

    /// <summary>
    /// Several reads that agree with each other, without the write lock (#636). Deferred: Microsoft.Data.Sqlite issues
    /// a plain BEGIN, and in WAL mode the first read pins a snapshot that neither blocks the workers' writes nor waits
    /// for them. <see cref="InTransactionAsync{T}"/> is BEGIN IMMEDIATE - right for a claim, wrong for a screen that
    /// polls: it queues behind every writer and makes every writer queue behind it.
    /// </summary>
    public async Task<T> ReadAsync<T>(Func<SqliteConnection, SqliteTransaction, T> read, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            uow.BeginRead();
            return read(uow.Connection, uow.ReadTransaction());
        }
    }

    internal static int Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, transaction, sql, parameters);
        return command.ExecuteNonQuery();
    }

    internal static object? Scalar(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, transaction, sql, parameters);
        return command.ExecuteScalar();
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql, (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    private void RecordQueueDepth(SqliteConnection connection, SqliteTransaction transaction)
    {
        if (ReferenceEquals(_metrics, NoJobQueueMetrics.Instance))
        {
            return;
        }

        var depth = Scalar(
            connection,
            transaction,
            "SELECT count(*) FROM jobs WHERE status = @pending OR status = @leased",
            ("@pending", ProcessingJobStatus.Pending),
            ("@leased", ProcessingJobStatus.Leased));
        _metrics.SetQueueDepth(MetricsModule, (int)Convert.ToInt64(depth, CultureInfo.InvariantCulture));
    }
}
