namespace Weir.Tray;

/// <summary>
/// How the tray ends its run. The server is always stopped first, cleanly (ServerProcessStop), so its running jobs
/// finish and its database closes; only then is a downloaded update handed to Velopack, whose installer stops
/// whatever of Weir it still finds running (#857). Before either, while the server still runs, it is asked to save a
/// copy of Weir's data (<paramref name="backup"/>); an update whose copy could not be saved is not applied.
/// </summary>
sealed class TrayShutdown(Func<Task> stopServer, IUpdateService updates, IUpdateBackup? backup = null)
{
    /// <summary>
    /// Stops the server. A downloaded update is then installed, which ends this process, unless its copy of the data could
    /// not be saved: then Weir quits and the update waits. With none downloaded, this returns and the caller ends the tray.
    /// </summary>
    internal async Task QuitAsync()
    {
        var mayApply = updates.IsDownloaded && await SavedBeforeApplyAsync().ConfigureAwait(false);
        await stopServer();
        if (mayApply)
        {
            TrayLog.Write("Installing the downloaded update now that Weir has quit.");
            updates.ApplyAndExit();
        }
    }

    /// <summary>
    /// Stops the server, installs the downloaded update and starts Weir again. Does nothing, and leaves the server
    /// running, when nothing has been downloaded or when its copy of the data could not be saved first.
    /// </summary>
    internal async Task RestartToUpdateAsync()
    {
        if (!updates.IsDownloaded || !await SavedBeforeApplyAsync().ConfigureAwait(false))
        {
            return;
        }
        await stopServer();
        updates.ApplyAndRestart();
    }

    private Task<bool> SavedBeforeApplyAsync() =>
        backup is null ? Task.FromResult(true) : backup.SaveBeforeApplyAsync(updates.PendingVersion);
}
