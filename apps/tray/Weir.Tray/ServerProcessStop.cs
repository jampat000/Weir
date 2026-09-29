using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Weir.Tray;

/// <summary>How a server process came to be stopped.</summary>
enum ServerStopOutcome
{
    AlreadyExited,

    /// <summary>It was asked to stop and shut down on its own.</summary>
    StoppedCleanly,

    /// <summary>It did not stop when asked, or could not be asked, and its process tree was killed.</summary>
    Killed,

    /// <summary>It did not stop when asked and could not be killed either.</summary>
    KillFailed,
}

/// <summary>How long a stop waits for the server to exit on its own, and then for a kill to take effect.</summary>
readonly record struct ServerStopTimeouts(TimeSpan Graceful, TimeSpan AfterKill)
{
    internal static ServerStopTimeouts Default { get; } = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
}

/// <summary>
/// Stopping the bundled server process: it is asked to stop (ServerStopRequest) so it shuts down through its host and
/// finishes running jobs and closes its database; only when it does not exit in time is its whole process tree killed.
/// </summary>
static class ServerProcessStop
{
    internal static Task<ServerStopOutcome> StopAsync(Process process) => StopAsync(process, ServerStopTimeouts.Default);

    internal static async Task<ServerStopOutcome> StopAsync(Process process, ServerStopTimeouts timeouts)
    {
        if (process.HasExited)
        {
            return ServerStopOutcome.AlreadyExited;
        }
        var started = Stopwatch.GetTimestamp();
        TrayLog.Write($"Stopping bundled server host pid={process.Id}");
        if (!ServerStopRequest.TrySend(process.Id, out var failure))
        {
            TrayLog.Write($"Bundled server host pid={process.Id} cannot be asked to stop ({failure}); killing it");
            return await KillAsync(process, timeouts.AfterKill).ConfigureAwait(false);
        }
        if (await ExitsWithinAsync(process, timeouts.Graceful).ConfigureAwait(false))
        {
            TrayLog.Write($"Bundled server host pid={process.Id} stopped cleanly in {Seconds(Stopwatch.GetElapsedTime(started))}");
            return ServerStopOutcome.StoppedCleanly;
        }
        TrayLog.Write($"Bundled server host pid={process.Id} did not stop in {Seconds(timeouts.Graceful)}; killing it");
        return await KillAsync(process, timeouts.AfterKill).ConfigureAwait(false);
    }

    private static async Task<ServerStopOutcome> KillAsync(Process process, TimeSpan wait)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            await ExitsWithinAsync(process, wait).ConfigureAwait(false);
            return ServerStopOutcome.Killed;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
        {
            TrayLog.Write($"Could not kill the server process pid={process.Id}: {ex.Message}");
            return ServerStopOutcome.KillFailed;
        }
    }

    private static async Task<bool> ExitsWithinAsync(Process process, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    internal static string Seconds(TimeSpan duration) => $"{duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s";
}
