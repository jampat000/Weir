using Microsoft.Data.Sqlite;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite;

/// <summary>
/// On Linux, deleting or replacing the database file does not stop a connection that already had it open: the
/// file stays reachable through any descriptor opened before the change, including an idle handle sitting in
/// <see cref="SqliteDatabase"/>'s pool. <see cref="SqliteDatabase.OpenAsync"/> must refuse to hand that handle
/// to the next caller once the file on disk no longer matches the one the pool was filled with.
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
            // platform difference; the identity check under test compares recorded state against disk regardless
            // of whether the stale handle is still literally open.
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

    [Fact]
    public async Task An_open_after_the_file_is_replaced_is_refused_and_does_not_reuse_the_pooled_handle()
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
            using (var replacement = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                replacement.Open();
            }

            // NTFS tunnels a deleted file's creation time onto one recreated under the same name moments later,
            // which would otherwise hide this exact replacement on Windows; back-dating it is what a file that
            // was genuinely created at a different, unrelated time looks like.
            File.SetCreationTimeUtc(path, DateTime.UtcNow.AddDays(-1));

            var exception = await Assert.ThrowsAsync<SqliteException>(async () => await database.OpenAsync());
            Assert.Equal(14, exception.SqliteErrorCode);

            await using var afterRefusal = await database.OpenAsync();
            Assert.NotSame(staleHandle, afterRefusal.Handle);
        }
        finally
        {
            database.ClearPool();
        }
    }
}
