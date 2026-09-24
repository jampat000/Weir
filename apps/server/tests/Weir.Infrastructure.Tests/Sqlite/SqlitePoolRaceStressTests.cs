using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Weir.Infrastructure.Sqlite;
using Xunit.Abstractions;

namespace Weir.Infrastructure.Tests.Sqlite;

/// <summary>
/// The pool race itself, without reflection, provoked the way it was first found in CI — several threads opening
/// at once on a pool that is still growing. Every round starts a brand-new pool, lets eight threads open together
/// and hold their connections, and checks that no two of them were given the same native handle.
/// </summary>
/// <remarks>
/// Before the gate, <see cref="SqliteDatabase.Open"/> shared a handle 8 times in 4,599 rounds on a 16-thread Windows
/// machine (8 of 12 thirty-second runs); with it, none in 14,723 rounds. <c>WEIR_640_RAW=1</c>
/// opens <see cref="SqliteConnection"/> directly instead, past the gate, and still shares a handle within a few
/// seconds — the race is Microsoft.Data.Sqlite's and is still there underneath. <c>WEIR_640_SECONDS</c> sets how long
/// to run (default 5).
/// </remarks>
/// <seealso href="https://github.com/jampat000/Weir/issues/640"/>
public sealed class SqlitePoolRaceStressTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Stress")]
    public void Concurrent_opens_on_a_growing_pool_never_share_a_native_handle()
    {
        var seconds = double.Parse(Environment.GetEnvironmentVariable("WEIR_640_SECONDS") ?? "5", CultureInfo.InvariantCulture);
        var raw = Environment.GetEnvironmentVariable("WEIR_640_RAW") == "1";
        const int threads = 8;

        using var temp = new TempDirectory();
        var database = new SqliteDatabase(temp.Join("weir.sqlite3"));
        using (database.Open())
        {
        }

        var inUse = new ConcurrentDictionary<object, int>(ReferenceEqualityComparer.Instance);
        var shared = 0;
        var errors = new ConcurrentQueue<string>();
        var rounds = 0;
        var phases = 0;
        var clock = Stopwatch.StartNew();
        var stop = false;
        using var barrier = new Barrier(threads, _ =>
        {
            // Three phases a round: all start opening, all hold a connection, all have closed it.
            if (++phases % 3 != 0)
            {
                return;
            }

            database.ClearPool();
            rounds++;
            if (clock.Elapsed.TotalSeconds >= seconds || Volatile.Read(ref shared) > 0)
            {
                Volatile.Write(ref stop, true);
            }
        });

        var workers = Enumerable.Range(0, threads).Select(index => new Thread(() =>
        {
            do
            {
                barrier.SignalAndWait();

                SqliteConnection? connection = null;
                object? handle = null;
                try
                {
                    if (raw)
                    {
                        connection = new SqliteConnection(database.ConnectionString);
                        connection.Open();
                    }
                    else
                    {
                        connection = database.Open();
                    }

                    handle = connection.Handle!;
                    if (!inUse.TryAdd(handle, index))
                    {
                        Interlocked.Increment(ref shared);
                        handle = null;
                    }
                }
                catch (Exception exception)
                {
                    errors.Enqueue(exception.GetType().Name + ": " + exception.Message);
                }

                barrier.SignalAndWait();
                if (handle is not null)
                {
                    inUse.TryRemove(new KeyValuePair<object, int>(handle, index));
                }

                try
                {
                    connection?.Dispose();
                }
                catch (Exception exception)
                {
                    errors.Enqueue("dispose " + exception.GetType().Name + ": " + exception.Message);
                }

                barrier.SignalAndWait();
            }
            while (!Volatile.Read(ref stop));
        })
        { IsBackground = true, Name = $"issue640-{index}" }).ToList();

        workers.ForEach(worker => worker.Start());
        workers.ForEach(worker => worker.Join());

        output.WriteLine($"raw={raw} rounds={rounds} shared={shared} errors={errors.Count} elapsed={clock.Elapsed.TotalSeconds:F1}s");
        foreach (var error in errors.Distinct().Take(10))
        {
            output.WriteLine(error);
        }

        Assert.Equal(0, shared);
    }
}
