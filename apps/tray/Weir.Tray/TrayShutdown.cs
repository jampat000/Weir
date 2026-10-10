namespace Weir.Tray;

/// <summary>
/// How the tray ends its run. The server is always stopped first, cleanly (ServerProcessStop), so its running jobs
/// finish and its database closes; only then is a downloaded update handed to Velopack, whose installer stops
/// whatever of Weir it still finds running (#857). Before either, while the server still runs, it is asked to save a
/// copy of Weir's data (<paramref name="backup"/>); an update whose copy could not be saved is not applied.
/// </summary>
/// <remarks>
/// One of these runs at a time. The idle install, a balloon click, the apply-now flag and Quit can arrive together, and two of
/// them would share one request for a copy and could start Velopack twice; the second returns at once, having done nothing.
/// </remarks>
sealed class TrayShutdown(Func<Task> stopServer, IUpdateService updates, IUpdateBackup? backup = null)
{
    private int _underWay;

    /// <summary>
    /// Stops the server. A downloaded update is then installed, which ends this process, unless its copy of the data could
    /// not be saved: then Weir quits and the update waits. With none downloaded, this returns and the caller ends the tray.
    /// False, having done nothing, when another of these is under way.
    /// </summary>
    internal async Task<bool> QuitAsync()
    {
        if (!TryBegin("quit"))
        {
            return false;
        }
        try
        {
            var mayApply = updates.IsDownloaded && await SavedBeforeApplyAsync().ConfigureAwait(false);
            await stopServer();
            if (mayApply)
            {
                TrayLog.Write("Installing the downloaded update now that Weir has quit.");
                updates.ApplyAndExit();
            }
            return true;
        }
        finally
        {
            Volatile.Write(ref _underWay, 0);
        }
    }

    /// <summary>
    /// Stops the server, installs the downloaded update and starts Weir again. Does nothing, and leaves the server
    /// running, when nothing has been downloaded or when its copy of the data could not be saved first. False, having
    /// done nothing, when another of these is under way.
    /// </summary>
    internal async Task<bool> RestartToUpdateAsync()
    {
        if (!TryBegin("restart to update"))
        {
            return false;
        }
        try
        {
            if (updates.IsDownloaded && await SavedBeforeApplyAsync().ConfigureAwait(false))
            {
                await stopServer();
                updates.ApplyAndRestart();
            }
            return true;
        }
        finally
        {
            Volatile.Write(ref _underWay, 0);
        }
    }

    private bool TryBegin(string what)
    {
        if (Interlocked.CompareExchange(ref _underWay, 1, 0) == 0)
        {
            return true;
        }
        TrayLog.Write($"An update is already being applied, so this request to {what} is ignored.");
        return false;
    }

    // A copy that cannot be asked for is a copy that was not saved, whatever the reason.
    private async Task<bool> SavedBeforeApplyAsync()
    {
        if (backup is null)
        {
            return true;
        }
        try
        {
            return await backup.SaveBeforeApplyAsync(updates.PendingVersion).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Whatever the hook throws, the update is not applied and the caller still stops the server.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            TrayLog.Write($"Could not make sure a copy of Weir's data was saved first, so the update is not applied:\n{ex}");
            return false;
        }
    }
}
