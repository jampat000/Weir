using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SQLitePCL;

namespace Weir.Infrastructure.Sqlite;

/// <summary>
/// Opens connections to Weir's SQLite file with the same PRAGMAs on every connection: WAL journal,
/// foreign keys on, a 30 second busy timeout and <c>synchronous=NORMAL</c>, plus the cache and journal
/// settings in <see cref="TuningPragmas"/>.
/// </summary>
public sealed class SqliteDatabase
{
    /// <summary>
    /// Processing writes progress in short transactions; a transient writer collision should wait for
    /// that transaction rather than fail a completed media mutation.
    /// </summary>
    /// <remarks>
    /// The contract suite's HTTP client timeout (<c>REQUEST_TIMEOUT_S</c> in its support client module) is
    /// held above this value, 45s against 30s. If the two were equal, a request blocked on the write lock
    /// would have the client give up at the same instant this ceiling expires, and the failure would show up
    /// as a bare <c>httpx.ReadTimeout</c> with no server-side error to go with it (#586). If this value
    /// changes, move that one too, and keep it the larger of the pair.
    /// </remarks>
    public const int BusyTimeoutMilliseconds = 30_000;

    /// <summary>
    /// One gate per connection pool (Microsoft.Data.Sqlite keys its pools by the exact connection string), held
    /// while a pooled handle is handed out and while the pool is cleared (#640).
    /// </summary>
    /// <remarks>
    /// Microsoft.Data.Sqlite takes a handle from its pool under the pool's lock but activates it outside that lock,
    /// and activation marks the handle in use (<c>_active = true</c>) a moment before it records who holds it. An
    /// open on another thread that finds the pool empty with an even count first reclaims "leaked" connections —
    /// ones in use that nobody holds — and a handle in that moment is one. It goes back on the idle stack and out
    /// again to the second opener, so two connections share one native handle: one's <c>BEGIN IMMEDIATE</c> is
    /// the other's open transaction, and each later returns the handle to the pool while the other may still be
    /// using it. The reclaim only runs inside another open or inside a pool clear, so no open or clear of the same
    /// pool may run while a handle is being activated.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, Lock> PoolGates = new(StringComparer.Ordinal);

    /// <summary>
    /// Settings that stay with a native connection, so they are applied once per handle rather than on every
    /// pooled open (#711): an 8 MB page cache instead of the 2 MB default, temporary tables and sorts in memory,
    /// reads through a 256 MB memory map, and a WAL cut back to 64 MB after each checkpoint instead of keeping
    /// its largest size for ever.
    /// </summary>
    private const string TuningPragmas =
        "PRAGMA cache_size=-8000; PRAGMA temp_store=MEMORY; PRAGMA mmap_size=268435456; PRAGMA journal_size_limit=67108864;";

    /// <summary>The native handles <see cref="TuningPragmas"/> has run on; an entry goes when its handle is closed and collected.</summary>
    private static readonly ConditionalWeakTable<sqlite3, object> TunedHandles = [];

    private static readonly object TunedMarker = new();

    private readonly Lock? _poolGate;
    private readonly ILogger? _logger;
    private readonly int _busyTimeoutMilliseconds;

    /// <param name="databasePath">The SQLite file.</param>
    /// <param name="pooling">
    /// Off only for a caller deliberately simulating several independent processes sharing one file in a
    /// single test process (e.g. a claim-concurrency stress test): every <see cref="SqliteDatabase"/>
    /// instance made from the same path otherwise shares one process-wide pool keyed by connection
    /// string, which is exactly right for production (repeated opens reuse a warm native handle) but
    /// means "separate workers" in such a test are not actually isolated the way separate real processes
    /// would be — each gets a genuinely fresh, unshared connection instead.
    /// </param>
    /// <param name="logger">Where a pooled connection found still inside a transaction is reported.</param>
    /// <param name="busyTimeoutMilliseconds">
    /// Overrides <see cref="BusyTimeoutMilliseconds"/>. Production never passes this; a test that needs a
    /// write-lock wait to fail fast, instead of tying up thirty real seconds, can shrink it here.
    /// </param>
    public SqliteDatabase(string databasePath, bool pooling = true, ILogger<SqliteDatabase>? logger = null, int? busyTimeoutMilliseconds = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);
        DatabasePath = databasePath;
        _logger = logger;
        _busyTimeoutMilliseconds = busyTimeoutMilliseconds ?? BusyTimeoutMilliseconds;
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = pooling,
            // Seconds a command waits on a locked database (complements PRAGMA busy_timeout). It should
            // only ever time a genuine wait for another writer's lock. Microsoft.Data.Sqlite re-runs a
            // statement that answers SQLITE_BUSY until this expires, and SQLITE_BUSY_SNAPSHOT (a
            // read-then-write upgrade on a snapshot another connection has already overtaken) answers
            // exactly that while never being able to succeed, wasting the whole thirty seconds. Units of
            // work take the write lock at BEGIN (UnitOfWork.EnsureTransaction), so no transaction here can
            // hold a stale snapshot (#586).
            DefaultTimeout = _busyTimeoutMilliseconds / 1000,
        }.ToString();
        _poolGate = pooling ? PoolGates.GetOrAdd(ConnectionString, static _ => new Lock()) : null;
    }

    public string DatabasePath { get; }

    public string ConnectionString { get; }

    /// <summary>
    /// Releases this database's own pooled connections, e.g. so its file can be deleted once every
    /// caller is done with it. Scoped to <see cref="ConnectionString"/> alone: unlike
    /// <see cref="SqliteConnection.ClearAllPools"/>, it never touches another database's pool, so it is
    /// safe to call while other connections (elsewhere in the process, such as a concurrently running
    /// test) are still open.
    /// </summary>
    public void ClearPool()
    {
        using var connection = new SqliteConnection(ConnectionString);
        if (_poolGate is null)
        {
            SqliteConnection.ClearPool(connection);
            return;
        }

        lock (_poolGate)
        {
            SqliteConnection.ClearPool(connection);
        }
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        // SqliteConnection.OpenAsync is Open() behind a completed task, so nothing is lost by opening synchronously.
        cancellationToken.ThrowIfCancellationRequested();
        var connection = OpenOutsideAnyTransaction();
        try
        {
            await ApplyPragmasAsync(connection, _busyTimeoutMilliseconds, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public SqliteConnection Open()
    {
        var connection = OpenOutsideAnyTransaction();
        try
        {
            ApplyPragmasAsync(connection, _busyTimeoutMilliseconds, CancellationToken.None).GetAwaiter().GetResult();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Dispose <paramref name="connection"/> so that its native handle is closed instead of going back to the pool,
    /// which ends any transaction still open on it: SQLite rolls back what a closed connection left open.
    /// </summary>
    internal void CloseHandle(SqliteConnection connection)
    {
        // That rollback calls any rollback hook still registered on the handle, from inside sqlite3_close_v2. The
        // one Microsoft.Data.Sqlite registers for a live SqliteTransaction touches the handle being closed and
        // throws out through the native frame, which on Linux takes the process down.
        raw.sqlite3_rollback_hook(connection.Handle, null, null);

        // Cleared first, the pool disposes the handle when it comes back rather than keeping it.
        ClearPool();
        connection.Dispose();
    }

    /// <summary>
    /// A pooled handle that is not inside a transaction. Microsoft.Data.Sqlite does nothing to a handle that comes
    /// back to its pool, so one whose last user left a transaction open — a ROLLBACK that failed, a connection
    /// shared by two users (<see cref="PoolGates"/>) — still has it, and after a <c>BEGIN IMMEDIATE</c> still holds
    /// the write lock that every other writer is queueing for (#640).
    /// </summary>
    private SqliteConnection OpenOutsideAnyTransaction()
    {
        var connection = OpenPooledHandle();
        if (raw.sqlite3_get_autocommit(connection.Handle) != 0)
        {
            return connection;
        }

        _logger?.LogWarning(
            "A pooled connection to {DatabasePath} was handed out still inside a transaction; Weir rolled it back " +
            "before using the connection.",
            DatabasePath);
        try
        {
            // If that transaction still has an owner, the rollback hook Microsoft.Data.Sqlite registered for it marks
            // it rolled back, and the owner's next command fails instead of running outside any transaction.
            using var rollback = connection.CreateCommand();
            rollback.CommandText = "ROLLBACK";
            rollback.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Checked below.
        }

        if (raw.sqlite3_get_autocommit(connection.Handle) != 0)
        {
            return connection;
        }

        CloseHandle(connection);
        return OpenPooledHandle();
    }

    private SqliteConnection OpenPooledHandle()
    {
        var connection = new SqliteConnection(ConnectionString);
        try
        {
            if (_poolGate is null)
            {
                connection.Open();
            }
            else
            {
                lock (_poolGate)
                {
                    connection.Open();
                }
            }

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The health probe: the database file is still there, and <c>SELECT 1</c> answers with a one second
    /// busy timeout, so a long writer makes health slow for at most a second instead of thirty. Never throws.
    /// </summary>
    /// <remarks>
    /// The file check comes first because a pooled connection keeps working on a file that has been deleted
    /// or replaced underneath it (Linux lets an open file be unlinked), so <c>SELECT 1</c> alone would report
    /// a database that no new connection can open as healthy.
    /// </remarks>
    public async Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(DatabasePath))
        {
            return false;
        }

        try
        {
            var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await ExecuteAsync(connection, "PRAGMA busy_timeout=1000", cancellationToken).ConfigureAwait(false);
                try
                {
                    await ExecuteAsync(connection, "SELECT 1", cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await ExecuteAsync(connection, $"PRAGMA busy_timeout={BusyTimeoutMilliseconds}", CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task ApplyPragmasAsync(SqliteConnection connection, int busyTimeoutMilliseconds, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA foreign_keys=ON", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, $"PRAGMA busy_timeout={busyTimeoutMilliseconds}", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA synchronous=NORMAL", cancellationToken).ConfigureAwait(false);
        var handle = connection.Handle!;
        if (!TunedHandles.TryGetValue(handle, out _))
        {
            await ExecuteAsync(connection, TuningPragmas, cancellationToken).ConfigureAwait(false);
            TunedHandles.AddOrUpdate(handle, TunedMarker);
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
