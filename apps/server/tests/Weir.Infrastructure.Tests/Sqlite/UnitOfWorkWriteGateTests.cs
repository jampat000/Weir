using Microsoft.Data.Sqlite;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Jobs;

namespace Weir.Infrastructure.Tests.Sqlite;

/// <summary>
/// Weir's own writers queue for the write lock without holding a thread, one at a time and in the order they asked, and a
/// unit of work gives its turn back however it ends.
/// </summary>
public sealed class UnitOfWorkWriteGateTests
{
    private const string InsertJob =
        "INSERT INTO jobs (dedupe_key, job_kind, status, max_attempts) VALUES (@dedupe, 'processing.remux_pass', 'pending', 3)";

    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(20);

    private static async Task WriteAsync(SqliteDatabase database, string dedupeKey, CancellationToken cancellationToken = default)
    {
        var uow = await UnitOfWork.OpenAsync(database, cancellationToken);
        await using (uow.ConfigureAwait(false))
        {
            await uow.ExecuteAsync(InsertJob, ("@dedupe", dedupeKey));
            await uow.CommitAsync();
        }
    }

    /// <summary>A unit that has written and keeps its turn until <paramref name="release"/> completes.</summary>
    private static async Task<Task> HoldTheLockAsync(SqliteDatabase database, string dedupeKey, TaskCompletionSource release)
    {
        var holding = new TaskCompletionSource();
        var holder = Task.Run(async () =>
        {
            var uow = await UnitOfWork.OpenAsync(database);
            await using (uow.ConfigureAwait(false))
            {
                await uow.ExecuteAsync(InsertJob, ("@dedupe", dedupeKey));
                holding.SetResult();
                await release.Task;
                await uow.CommitAsync();
            }
        });
        await holding.Task.WaitAsync(Generous);
        return holder;
    }

    [Fact]
    public async Task Writers_waiting_for_the_lock_hold_no_threads_and_go_in_the_order_they_asked()
    {
        using var db = new JobsTestDatabase();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = await HoldTheLockAsync(db.Database, "holder", release);

        // More than the pool's minimum: threads blocked in SQLite's busy handler would stall everything else.
        var waiting = Enumerable.Range(0, Environment.ProcessorCount * 4 + 8).ToArray();
        var writers = waiting.Select(index => WriteAsync(db.Database, $"waiting-{index}")).ToArray();

        Assert.Equal(42, await Task.Run(() => 42).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.All(writers, writer => Assert.False(writer.IsCompleted));

        release.SetResult();
        await Task.WhenAll(writers.Append(holder)).WaitAsync(Generous);

        var order = new List<string>();
        await using (var read = await UnitOfWork.OpenAsync(db.Database))
        {
            order.AddRange(await read.QueryAsync("SELECT dedupe_key FROM jobs ORDER BY id", reader => reader.GetString(0)));
        }

        Assert.Equal(waiting.Select(index => $"waiting-{index}").Prepend("holder"), order);
    }

    [Fact]
    public async Task A_unit_that_fails_gives_its_turn_to_the_next_writer()
    {
        using var db = new JobsTestDatabase();

        await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            await using var uow = await UnitOfWork.OpenAsync(db.Database);
            await uow.ExecuteAsync("INSERT INTO no_such_table VALUES (1)");
        });

        await WriteAsync(db.Database, "next").WaitAsync(Generous);
        Assert.Equal(1L, db.Count("SELECT count(*) FROM jobs"));
    }

    [Fact]
    public async Task A_unit_disposed_without_committing_gives_its_turn_to_the_next_writer()
    {
        using var db = new JobsTestDatabase();
        await using (var uow = await UnitOfWork.OpenAsync(db.Database))
        {
            await uow.ExecuteAsync(InsertJob, ("@dedupe", "abandoned"));
        }

        await WriteAsync(db.Database, "next").WaitAsync(Generous);
        Assert.Equal(1L, db.Count("SELECT count(*) FROM jobs"));
    }

    [Fact]
    public async Task A_unit_that_commits_gives_its_turn_up_before_its_after_commit_callbacks_run()
    {
        using var db = new JobsTestDatabase();
        Task? nextWriter = null;
        await using (var uow = await UnitOfWork.OpenAsync(db.Database))
        {
            await uow.ExecuteAsync(InsertJob, ("@dedupe", "first"));
            uow.OnCommitted(() => nextWriter = WriteAsync(db.Database, "from-the-callback"));
            await uow.CommitAsync();
        }

        await nextWriter!.WaitAsync(Generous);
        Assert.Equal(2L, db.Count("SELECT count(*) FROM jobs"));
    }

    [Fact]
    public async Task A_unit_cancelled_after_it_wrote_gives_its_turn_to_the_next_writer()
    {
        using var db = new JobsTestDatabase();
        using var cancel = new CancellationTokenSource();
        await using (var uow = await UnitOfWork.OpenAsync(db.Database, cancel.Token))
        {
            await uow.ExecuteAsync(InsertJob, ("@dedupe", "cancelled"));
            await cancel.CancelAsync();
        }

        await WriteAsync(db.Database, "next").WaitAsync(Generous);
        Assert.Equal(1L, db.Count("SELECT count(*) FROM jobs"));
    }

    [Fact]
    public async Task A_writer_cancelled_while_it_waits_leaves_the_queue_without_taking_a_turn()
    {
        using var db = new JobsTestDatabase();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = await HoldTheLockAsync(db.Database, "holder", release);
        using var cancel = new CancellationTokenSource();
        var cancelled = WriteAsync(db.Database, "cancelled", cancel.Token);
        var next = WriteAsync(db.Database, "next");

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        release.SetResult();
        await Task.WhenAll(holder, next).WaitAsync(Generous);

        Assert.Equal(2L, db.Count("SELECT count(*) FROM jobs"));
    }

    [Fact]
    public async Task A_writer_still_waiting_when_the_busy_timeout_passes_fails_as_SQLite_would()
    {
        using var db = new JobsTestDatabase();
        var database = new SqliteDatabase(db.DbPath, busyTimeoutMilliseconds: 300);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = await HoldTheLockAsync(database, "holder", release);

        var refused = await Assert.ThrowsAsync<SqliteException>(() => WriteAsync(database, "too-late"));
        Assert.Equal(5, refused.SqliteErrorCode);

        release.SetResult();
        await holder.WaitAsync(Generous);
        await WriteAsync(database, "after").WaitAsync(Generous);
        Assert.Equal(2L, db.Count("SELECT count(*) FROM jobs"));
    }

    [Fact]
    public async Task A_second_unit_opened_after_the_first_commits_is_not_held_up()
    {
        using var db = new JobsTestDatabase();
        var outer = await UnitOfWork.OpenAsync(db.Database);
        await using (outer.ConfigureAwait(false))
        {
            await outer.CountAsync("SELECT count(*) FROM jobs");
            await outer.ExecuteAsync(InsertJob, ("@dedupe", "outer"));
            await outer.CommitAsync();

            await WriteAsync(db.Database, "inner").WaitAsync(Generous);
        }

        Assert.Equal(2L, db.Count("SELECT count(*) FROM jobs"));
    }

    /// <summary>
    /// A flow that opens a second writing unit while its first still holds the lock cannot succeed, with or without the gate: it
    /// is refused when the busy timeout passes, never left waiting for ever.
    /// </summary>
    [Fact]
    public async Task A_second_unit_that_writes_while_the_first_holds_the_lock_is_refused_not_hung()
    {
        using var db = new JobsTestDatabase();
        var database = new SqliteDatabase(db.DbPath, busyTimeoutMilliseconds: 300);
        var outer = await UnitOfWork.OpenAsync(database);
        await using (outer.ConfigureAwait(false))
        {
            await outer.ExecuteAsync(InsertJob, ("@dedupe", "outer"));

            var refused = await Assert.ThrowsAsync<SqliteException>(() => WriteAsync(database, "inner").WaitAsync(Generous));
            Assert.Equal(5, refused.SqliteErrorCode);

            await outer.CommitAsync();
        }

        Assert.Equal(1L, db.Count("SELECT count(*) FROM jobs"));
    }
}
