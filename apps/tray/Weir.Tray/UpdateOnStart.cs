namespace Weir.Tray;

/// <summary>
/// Installing an update that an earlier run downloaded and never got to install: Windows ended the session, or the
/// tray was killed, before the person quit Weir. It happens before the server starts, when nothing is running that
/// an install could interrupt (#857).
/// </summary>
static class UpdateOnStart
{
    internal const string AttemptFileName = "update-start-attempt";

    /// <summary>
    /// Installs the update left waiting, if there is one and the person's update choice allows an install. Returns
    /// true when the installer has been started and the tray must exit at once. Each update is tried once from
    /// here: an install that fails puts the old tray back, which would otherwise try again forever.
    /// </summary>
    internal static bool TryApply(IUpdateService updates, UpdateMode mode, string runtimeHome, Action stopOrphanedServers)
    {
        if (mode == UpdateMode.NotifyOnly || updates.FindUpdateLeftWaiting() is not { } version)
        {
            return false;
        }
        if (UpdateBackupHook.FailedFor(runtimeHome, version))
        {
            TrayLog.Write($"Update v{version} is waiting, but Weir could not save a copy of its data before applying it, and the server that starts after the update would fail the same way. Starting Weir as it is; the next update check tries again.");
            return false;
        }
        if (WasAttempted(runtimeHome, version))
        {
            TrayLog.Write($"Update v{version} is waiting, but installing it at start-up was tried before and did not finish. Starting Weir as it is.");
            return false;
        }
        if (!TryRecordAttempt(runtimeHome, version))
        {
            return false;
        }
        TrayLog.Write($"Update v{version} was left waiting to install. Installing it before the server starts.");
        stopOrphanedServers();
        updates.ApplyLeftWaitingUpdateAndRestart();
        return true;
    }

    // A record that cannot be read counts as an earlier attempt: without it a failing install could not be told
    // from a first one.
    private static bool WasAttempted(string runtimeHome, string version)
    {
        try
        {
            return File.ReadAllText(Path.Combine(runtimeHome, AttemptFileName)).Trim() == version;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TrayLog.Write($"Could not read {AttemptFileName} ({ex.Message}).");
            return true;
        }
    }

    private static bool TryRecordAttempt(string runtimeHome, string version)
    {
        try
        {
            AtomicFile.WriteAllText(runtimeHome, AttemptFileName, version);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TrayLog.Write($"Update v{version} is waiting, but Weir could not record trying it ({ex.Message}), so it is left for the next quit.");
            return false;
        }
    }
}
