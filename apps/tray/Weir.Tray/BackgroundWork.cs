namespace Weir.Tray;

/// <summary>
/// Runs the tray's background loops and the work its menu starts without waiting for it, so that a failure is
/// logged with what was running, not lost: nothing awaits these tasks, so an exception in one would otherwise
/// vanish, and one on a raw thread would end the tray.
/// </summary>
static class BackgroundWork
{
    /// <summary>Runs <paramref name="loop"/> on the thread pool until it returns, fails or is cancelled.</summary>
    internal static Task RunLoop(string name, Func<CancellationToken, Task> loop, CancellationToken cancellationToken) =>
        LogFailureAsync(name, Task.Run(() => loop(cancellationToken), CancellationToken.None), cancellationToken);

    /// <summary>Starts <paramref name="work"/> on the thread pool without waiting for it.</summary>
    internal static void Forget(string name, Func<Task> work) =>
        _ = LogFailureAsync(name, Task.Run(work), CancellationToken.None);

    /// <summary>Watches work already started (on the UI thread, say) without waiting for it.</summary>
    internal static void Observe(string name, Task started) =>
        _ = LogFailureAsync(name, started, CancellationToken.None);

    private static async Task LogFailureAsync(string name, Task work, CancellationToken cancellationToken)
    {
        try
        {
            await work.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TrayLog.Write($"{name} stopped: the tray is shutting down.");
        }
        catch (Exception ex)
        {
            // The one place a failure of work nobody awaits can be seen, so every kind is logged here.
            TrayLog.Write($"{name} stopped after an error:\n{ex}");
        }
    }
}
