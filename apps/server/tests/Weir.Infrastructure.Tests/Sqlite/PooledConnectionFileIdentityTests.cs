using Microsoft.Data.Sqlite;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite;

/// <summary>
/// On Linux, deleting the database file, or replacing it with a directory, does not stop a connection that
/// already had it open: the file stays reachable through any descriptor opened before the change, including an
/// idle handle sitting in <see cref="SqliteDatabase"/>'s pool. <see cref="SqliteDatabase.OpenAsync"/> must refuse
/// to hand that handle to the next caller once a regular file is no longer there.
/// </summary>
public sealed class PooledConnectionFileIdentityTests
{
    [Fact]
    public async Task A_pooled_handle_is_still_reused_when_the_file_is_unchanged()
    {
        using var temp = new TempDirectory();
        var database = new SqliteDatabase(temp.Join("weir.sqlite3"));
        try
        {
            var first = await database.OpenAsync();
            var handle = first.Handle;
            await first.DisposeAsync();

            await using var second = await database.OpenAsync();

            Assert.Same(handle, second.Handle);
        }
        finally
        {
            database.ClearPool();
        }
    }

    /// <summary>
    /// A healthy database keeps growing and checkpointing under a pool that never gets torn down; none of that
    /// ordinary activity may ever be mistaken for the file having gone missing.
    /// </summary>
    [Fact]
    public async Task A_healthy_database_keeps_opening_across_many_writes_and_a_wal_checkpoint()
    {
        using var temp = new TempDirectory();
        var database = new SqliteDatabase(temp.Join("weir.sqlite3"));
        try
        {
            await using (var setup = await database.OpenAsync())
            {
                await using var create = setup.CreateCommand();
                create.CommandText = "CREATE TABLE probe (value INTEGER)";
                await create.ExecuteNonQueryAsync();
            }

            const int writeCount = 50;
            for (var value = 0; value < writeCount; value++)
            {
                await using var connection = await database.OpenAsync();
                await using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO probe (value) VALUES ($value)";
                insert.Parameters.AddWithValue("$value", value);
                await insert.ExecuteNonQueryAsync();
            }

            await using (var checkpoint = await database.OpenAsync())
            {
                await using var command = checkpoint.CreateCommand();
                command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
                await command.ExecuteNonQueryAsync();
            }

            await using var afterCheckpoint = await database.OpenAsync();
            await using var count = afterCheckpoint.CreateCommand();
            count.CommandText = "SELECT count(*) FROM probe";
            Assert.Equal((long)writeCount, (long)(await count.ExecuteScalarAsync())!);
        }
        finally
        {
            database.ClearPool();
        }
    }

    [Fact]
    public async Task An_open_after_the_file_is_deleted_is_refused_and_does_not_reuse_the_pooled_handle()
    {
        using var temp = new TempDirectory();
        var path = temp.Join("weir.sqlite3");
        var database = new SqliteDatabase(path);
        try
        {
            var first = await database.OpenAsync();
            var staleHandle = first.Handle;
            await first.DisposeAsync();

            // Windows keeps a delete from succeeding while a native handle still has the file open, unlike the
            // Linux unlink-while-open case this guards against; closing the handle here only works around that
            // platform difference, it does not change what the check under test is doing (a plain File.Exists
            // at open time, regardless of whether the stale handle is still literally open).
            database.ClearPool();
            File.Delete(path);

            var exception = await Assert.ThrowsAsync<SqliteException>(async () => await database.OpenAsync());
            Assert.Equal(14, exception.SqliteErrorCode);

            // The refusal cleared the pool, so the next open (which recreates the file, same as any real caller
            // opening the database fresh) gets a genuinely new native handle rather than the stale one.
            await using var afterRefusal = await database.OpenAsync();
            Assert.NotSame(staleHandle, afterRefusal.Handle);
        }
        finally
        {
            database.ClearPool();
        }
    }

    /// <summary>Mirrors what <c>WeirTestServer.BreakDatabaseAsync</c> does to reproduce the failure on Linux.</summary>
    [Fact]
    public async Task An_open_after_the_file_is_replaced_by_a_directory_is_refused_and_does_not_reuse_the_pooled_handle()
    {
        using var temp = new TempDirectory();
        var path = temp.Join("weir.sqlite3");
        var database = new SqliteDatabase(path);
        try
        {
            var first = await database.OpenAsync();
            var staleHandle = first.Handle;
            await first.DisposeAsync();

            // See the sibling deletion test for why this is needed only to work around Windows file locking.
            database.ClearPool();
            File.Delete(path);
            Directory.CreateDirectory(path);

            var exception = await Assert.ThrowsAsync<SqliteException>(async () => await database.OpenAsync());
            Assert.Equal(14, exception.SqliteErrorCode);

            Directory.Delete(path);
            await using var afterRefusal = await database.OpenAsync();
            Assert.NotSame(staleHandle, afterRefusal.Handle);
        }
        finally
        {
            database.ClearPool();
        }
    }
}
