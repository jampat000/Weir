namespace Weir.Infrastructure.Runtime;

/// <summary>
/// Runs work that holds its thread for a long time (a copy of a multi-gigabyte file between volumes, a hash of one, a walk
/// of a share that has gone slow) on a thread of its own, so the thread pool keeps serving requests, the live stream and the
/// workers. The pool adds a thread only every half second or so once its minimum is in use, which a few minutes-long
/// blocking calls on pool threads turn into a stalled server.
/// </summary>
internal static class BlockingWork
{
    public static Task RunAsync(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public static Task<T> RunAsync<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
}
