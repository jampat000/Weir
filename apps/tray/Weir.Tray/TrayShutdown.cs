namespace Weir.Tray;

/// <summary>
/// How the tray ends its run. The server is always stopped first, cleanly (ServerProcessStop), so its running jobs
/// finish and its database closes; only then is a downloaded update handed to Velopack, whose installer stops
/// whatever of Weir it still finds running (#857).
/// </summary>
sealed class TrayShutdown(Func<Task> stopServer, IUpdateService updates)
{
    /// <summary>
    /// Stops the server. A downloaded update is then installed, which ends this process. With none, this returns
    /// and the caller ends the tray.
    /// </summary>
    internal async Task QuitAsync()
    {
        await stopServer();
        if (updates.IsDownloaded)
        {
            TrayLog.Write("Installing the downloaded update now that Weir has quit.");
            updates.ApplyAndExit();
        }
    }

    /// <summary>
    /// Stops the server, installs the downloaded update and starts Weir again. Does nothing, and leaves the server
    /// running, when nothing has been downloaded.
    /// </summary>
    internal async Task RestartToUpdateAsync()
    {
        if (!updates.IsDownloaded)
        {
            return;
        }
        await stopServer();
        updates.ApplyAndRestart();
    }
}
