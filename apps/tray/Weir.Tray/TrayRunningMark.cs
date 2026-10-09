using System.Security;
using Microsoft.Win32;

namespace Weir.Tray;

/// <summary>
/// Whether a tray was running when Setup stopped it. Setup stops the running Weir before the new version's after-install
/// hook runs, so by then there is no process left to ask. A tray sets the mark when it starts and clears it when it exits
/// in order (Quit, an update, the end of the Windows session). A mark that is still there was left by something that
/// ended the tray without its knowing: Setup, or a crash.
///
/// The key is volatile, so a restart or a sign-out never leaves a mark for a Weir that is not running. It sits directly
/// under Software because Windows makes every key created above a volatile one volatile too, and a stable key can't
/// then be created beneath it.
/// </summary>
sealed class TrayRunningMark(string keyPath)
{
    private const string DefaultKeyPath = @"SOFTWARE\WeirTrayRunning";
    private const string ProcessIdValueName = "ProcessId";

    /// <summary>The current user's mark.</summary>
    internal static TrayRunningMark ForThisUser() => new(DefaultKeyPath);

    // The hooks call these inside a time budget. A registry error is logged, never thrown: a missing mark costs Weir a
    // restart after a silent install, and failing the install over it would cost far more.

    /// <summary>Records that the tray with this process id is running.</summary>
    internal void Set(int processId)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile);
            key.SetValue(ProcessIdValueName, processId, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            TrayLog.Write($"Could not record that Weir is running: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes the mark when it is this tray's own. Another tray that has started since has set its own, and an exiting
    /// tray must not take that away.
    /// </summary>
    internal void Clear(int processId)
    {
        try
        {
            if (Recorded() == processId)
            {
                Remove();
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            TrayLog.Write($"Could not clear the record that Weir is running: {ex.Message}");
        }
    }

    /// <summary>Whether a tray was left marked as running. Reading it removes it, so one mark answers one question.</summary>
    internal bool Take()
    {
        try
        {
            var marked = Recorded() is not null;
            Remove();
            return marked;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            TrayLog.Write($"Could not read whether Weir was running: {ex.Message}");
            return false;
        }
    }

    private int? Recorded()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(ProcessIdValueName) as int?;
    }

    private void Remove() => Registry.CurrentUser.DeleteSubKey(keyPath, throwOnMissingSubKey: false);
}
