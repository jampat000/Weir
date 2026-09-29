namespace Weir.Tray;

/// <summary>
/// What the tray needs from Velopack: finding and downloading updates, and handing a downloaded one over to be
/// installed. Downloading installs nothing. Velopack's installer waits for this process to exit and, after about a
/// minute, stops it and everything under it, so it is started only when this process is about to end, and only
/// after the server has been stopped cleanly (#857).
/// </summary>
interface IUpdateService
{
    bool IsInstalled { get; }

    bool HasPendingUpdate { get; }

    /// <summary>An update was downloaded during this run and is waiting to be installed.</summary>
    bool IsDownloaded { get; }

    string? PendingVersion { get; }

    Task<bool> CheckForUpdateAsync();

    Task<bool> DownloadUpdateAsync();

    /// <summary>
    /// The version of an update that an earlier run downloaded and never installed, kept on disk by Velopack, or null.
    /// </summary>
    string? FindUpdateLeftWaiting();

    /// <summary>Installs the update downloaded during this run without starting Weir again. Ends this process.</summary>
    void ApplyAndExit();

    /// <summary>Installs the update downloaded during this run and starts Weir again. Ends this process.</summary>
    void ApplyAndRestart();

    /// <summary>
    /// Installs the update an earlier run left waiting, with no window, and starts Weir again once this process has
    /// exited. The caller returns from Main straight after.
    /// </summary>
    void ApplyLeftWaitingUpdateAndRestart();
}
