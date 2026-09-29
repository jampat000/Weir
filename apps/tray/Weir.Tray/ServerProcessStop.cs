using System.ComponentModel;
using System.Diagnostics;

namespace Weir.Tray;

/// <summary>Stopping the bundled server process: a polite close first, then a kill of its whole process tree.</summary>
static class ServerProcessStop
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(5);

    internal static async Task StopAsync(Process process)
    {
        if (process.HasExited)
        {
            return;
        }
        TrayLog.Write($"Stopping bundled server host pid={process.Id}");
        process.CloseMainWindow();
        if (await ExitsWithinAsync(process, StopTimeout).ConfigureAwait(false))
        {
            return;
        }
        TrayLog.Write($"Bundled server host pid={process.Id} did not exit in time; killing it");
        try
        {
            process.Kill(entireProcessTree: true);
            await ExitsWithinAsync(process, KillWait).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
        {
            TrayLog.Write($"Could not kill the server process pid={process.Id}: {ex.Message}");
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
}
