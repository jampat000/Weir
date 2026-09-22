using System.Reflection;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Jobs;

namespace Weir.Infrastructure.Tests.Sqlite;

/// <summary>
/// #640: a pooled native handle must never reach the next caller while it is inside a transaction. SQLite
/// refuses <c>PRAGMA synchronous</c> only inside a transaction, so <see cref="SqliteDatabase.OpenAsync"/>
/// failing with "Safety level may not be changed inside a transaction" means the handle it was given had one
/// open. Microsoft.Data.Sqlite's pool does no reset when a handle comes back (<c>SqliteConnectionPool.Return</c>),
/// so whatever state the last owner left is what the next one gets.
/// </summary>
public sealed class Issue640PooledTransactionTests
{
    /// <summary>
    /// How CI got there with no statement failing (Test run 35688389454). <c>SqliteConnectionFactory.GetConnection</c>
    /// takes a handle from the pool under the pool's lock, then calls <c>SqliteConnectionInternal.Activate</c>
    /// outside it, and <c>Activate</c> sets <c>_active = true</c> before it records the owner
    /// (<c>_outerConnection.SetTarget</c>). For that instant the handle is "active with no owner", which is exactly
    /// the test <c>SqliteConnectionPool.ReclaimLeakedConnections</c> uses to find a connection its owner forgot to
    /// dispose. Another thread opening at that moment on an empty pool with an even count reclaims it, puts it back
    /// on the idle stack and takes it: two <see cref="SqliteConnection"/>s now drive one native handle, and each
    /// later returns it, so it goes back to the pool while the other is still inside its transaction.
    /// <para>
    /// The instant is frozen here with reflection; everything after it is the real code path.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_handle_reclaimed_while_it_is_being_handed_out_does_not_reach_the_next_caller_mid_transaction()
    {
        using var temp = new TempDirectory();
        var database = new SqliteDatabase(temp.Join("weir.sqlite3"));
        SqliteConnection? first = null;
        SqliteConnection? second = null;
        SqliteConnection? thief = null;
        SqliteTransaction? transaction = null;
        try
        {
            // Two connections in use and none idle: the pool's count is even, so its next open first looks for
            // connections it believes were leaked.
            first = await database.OpenAsync();
            second = await database.OpenAsync();

            // `first` is between `_active = true` and `_outerConnection.SetTarget(first)`.
            FreezeMidActivation(first);

            // Another enqueue opens at that moment: the pool reclaims `first`'s handle and hands it out again...
            thief = await database.OpenAsync();
            Assert.Same(first.Handle, thief.Handle);

            // ...and runs its unit of work on it (InTransactionAsync: BEGIN IMMEDIATE).
            transaction = thief.BeginTransaction(deferred: false);

            // `first` finishes its own work and disposes, so the shared handle goes back to the pool while
            // `thief` is still inside its transaction.
            first.Dispose();
            first = null;

            // The next enqueue must get a handle that is not inside anyone's transaction.
            var exception = await Record.ExceptionAsync(async () =>
            {
                var next = await database.OpenAsync();
                await using (next.ConfigureAwait(false))
                {
                    Assert.Equal(1, raw.sqlite3_get_autocommit(next.Handle));
                }
            });

            Assert.Null(exception);
        }
        finally
        {
            DisposeQuietly(transaction);
            DisposeQuietly(thief);
            DisposeQuietly(first);
            DisposeQuietly(second);
            database.ClearPool();
        }
    }

    /// <summary>
    /// The other way in, with nothing concurrent: a unit of work whose ROLLBACK fails.
    /// <c>SqliteTransaction.RollbackInternal</c> marks the transaction complete in a <c>finally</c> even when
    /// <c>ROLLBACK</c> throws, and <see cref="UnitOfWork.DisposeAsync"/> swallows that <see cref="SqliteException"/>
    /// on the understanding that "closing the connection discards the transaction anyway". With pooling it does not:
    /// closing returns the handle to the pool with its <c>BEGIN IMMEDIATE</c> still open and the write lock still
    /// held. SQLite's authorizer refusing the ROLLBACK stands in for any real ROLLBACK failure (an I/O error, no
    /// memory).
    /// </summary>
    [Fact]
    public async Task A_unit_of_work_whose_rollback_fails_does_not_pool_its_open_transaction()
    {
        using var db = new JobsTestDatabase();
        using var otherProcess = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db.DbPath, Pooling = false }.ToString());
        otherProcess.Open();

        var uow = await UnitOfWork.OpenAsync(db.Database);
        await uow.ExecuteAsync(
            "INSERT INTO jobs (dedupe_key, job_kind, status, max_attempts) VALUES ('abandoned', 'processing.remux_pass', 'pending', 3)");
        raw.sqlite3_set_authorizer(
            uow.Connection.Handle,
            (object _, int action, string param0, string _, string _, string _) =>
                action == raw.SQLITE_TRANSACTION && param0 == "ROLLBACK" ? raw.SQLITE_DENY : raw.SQLITE_OK,
            null);

        // The request fails before committing; disposing the unit of work is all that cleans up.
        await uow.DisposeAsync();

        // Checked before anything opens a pooled connection again: the lock must be gone by now, not at the next open.
        var writeLockFree = WriteSucceedsWithoutWaiting(otherProcess);
        var nextOpen = await Record.ExceptionAsync(async () =>
        {
            var next = await db.Database.OpenAsync();
            await using (next.ConfigureAwait(false))
            {
                Assert.Equal(1, raw.sqlite3_get_autocommit(next.Handle));
            }
        });

        Assert.Null(nextOpen);
        Assert.True(writeLockFree, "the abandoned unit of work still holds the write lock");
        Assert.Equal(0L, db.Count("SELECT count(*) FROM jobs WHERE dedupe_key = 'abandoned'"));
    }

    private static bool WriteSucceedsWithoutWaiting(SqliteConnection connection)
    {
        using var noWait = connection.CreateCommand();
        noWait.CommandText = "PRAGMA busy_timeout=0";
        noWait.ExecuteNonQuery();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 1;
        command.CommandText = "BEGIN IMMEDIATE; ROLLBACK;";
        try
        {
            command.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            return false;
        }
    }

    /// <summary>Leave <paramref name="connection"/>'s pooled handle marked active with no owner recorded.</summary>
    private static void FreezeMidActivation(SqliteConnection connection)
    {
        var inner = typeof(SqliteConnection)
            .GetField("_innerConnection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(connection)!;
        var owner = (WeakReference<SqliteConnection?>)inner.GetType()
            .GetField("_outerConnection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(inner)!;
        owner.SetTarget(null);
    }

    private static void DisposeQuietly(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch (Exception exception) when (exception is SqliteException or ObjectDisposedException or InvalidOperationException)
        {
            // A handle that was shared can already be closed or mid-transaction under the other owner.
        }
    }
}
