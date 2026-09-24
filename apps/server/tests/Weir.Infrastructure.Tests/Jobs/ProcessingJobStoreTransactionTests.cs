using Microsoft.Data.Sqlite;
using Weir.Infrastructure.Jobs;

namespace Weir.Infrastructure.Tests.Jobs;

/// <summary>
/// #745: <see cref="ProcessingJobStore.InTransactionAsync{T}"/> and <see cref="ProcessingJobStore.ReadAsync{T}"/>
/// are a calling convention over <c>UnitOfWork</c>, not a second transaction mechanism, so their commit,
/// rollback and locking behaviour should match it exactly.
/// </summary>
public sealed class ProcessingJobStoreTransactionTests : IDisposable
{
    private const string InsertJob =
        "INSERT INTO jobs (dedupe_key, job_kind, status, max_attempts) VALUES (@dedupe, 'processing.remux_pass', 'pending', 3)";

    private readonly JobsTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task InTransactionAsync_commits_every_statement_run_against_the_transaction_it_hands_out()
    {
        await _db.Store.InTransactionAsync((connection, transaction) =>
        {
            ProcessingJobStore.Execute(connection, transaction, InsertJob, ("@dedupe", "one"));
            ProcessingJobStore.Execute(connection, transaction, InsertJob, ("@dedupe", "two"));
            return true;
        });

        Assert.Equal(2L, _db.Count("SELECT count(*) FROM jobs"));
    }

    [Fact]
    public async Task InTransactionAsync_rolls_back_and_frees_the_write_lock_when_the_work_throws()
    {
        var thrown = await Record.ExceptionAsync(() => _db.Store.InTransactionAsync<bool>((connection, transaction) =>
        {
            ProcessingJobStore.Execute(connection, transaction, InsertJob, ("@dedupe", "orphaned"));
            throw new InvalidOperationException("the queue's own work failed before it could commit");
        }));

        Assert.IsType<InvalidOperationException>(thrown);
        Assert.Equal(0L, _db.Count("SELECT count(*) FROM jobs"));
        Assert.True(ForeignWriteSucceedsWithoutWaiting(), "a failed claim must not leave the write lock held");
    }

    [Fact]
    public async Task ReadAsync_never_takes_the_write_lock()
    {
        await _db.Store.EnqueueOrGetAsync("seed", "processing.remux_pass");

        var count = await _db.Store.ReadAsync((connection, transaction) =>
            ProcessingJobStore.Scalar(connection, transaction, "SELECT count(*) FROM jobs"));

        Assert.Equal(1L, Convert.ToInt64(count));
        Assert.True(ForeignWriteSucceedsWithoutWaiting(), "a read must not block a writer");
    }

    /// <summary>
    /// A one-second, no-wait write from a second connection: succeeds when nothing holds the write lock, fails
    /// fast (never the thirty-second retry) when something does.
    /// </summary>
    private bool ForeignWriteSucceedsWithoutWaiting()
    {
        using var connection = _db.Database.Open();
        using var noWait = connection.CreateCommand();
        noWait.CommandText = "PRAGMA busy_timeout=0";
        noWait.ExecuteNonQuery();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 1;
        command.CommandText = InsertJob;
        command.Parameters.AddWithValue("@dedupe", $"foreign-{Guid.NewGuid():N}");
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
}
