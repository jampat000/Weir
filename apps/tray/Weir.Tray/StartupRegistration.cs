using System.Security;
using Microsoft.Win32;

namespace Weir.Tray;

/// <summary>
/// Starts Weir at sign-in through the current user's Run key. It is off until the person says yes, when the first run asks
/// (<see cref="StartWithWindowsPrompt"/>) or from the tray menu's "Start with Windows". A sign-in start passes
/// <see cref="Program.NoBrowserArgument"/> because nobody asked to see Weir then (#638).
/// </summary>
sealed class StartupRegistration(string runKeyPath, string startupFolder, string? executable)
{
    private const string DefaultRunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Weir";
    private const string StartupShortcutName = "Weir.lnk";

    /// <summary>The current user's registration for the running Weir.</summary>
    internal static StartupRegistration ForThisUser() =>
        new(DefaultRunKeyPath, Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.ProcessPath);

    /// <summary>Whether Weir starts when the person signs in.</summary>
    internal bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(runKeyPath);
                return key?.GetValue(ValueName) is not null;
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                TrayLog.Write($"Could not read whether Weir starts with Windows: {ex.Message}");
                return false;
            }
        }
    }

    // The installer's hooks call these inside a time budget. A registry or file error is logged, never thrown: failing an
    // install or uninstall over a sign-in entry would leave Weir half-installed.

    /// <summary>Makes Weir start when the person signs in. Returns whether it is now set.</summary>
    internal bool Enable()
    {
        if (string.IsNullOrEmpty(executable))
        {
            return false;
        }
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(runKeyPath, writable: true);
            key.SetValue(ValueName, Command(executable));
            TrayLog.Write(@"Weir now starts with Windows (HKCU\Run).");

            // A startup-folder shortcut made by hand would start a second copy at sign-in; the Run key is the one entry.
            if (RemoveStartupShortcut())
            {
                TrayLog.Write("Removed the startup folder shortcut; the Run key starts Weir now.");
            }
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            TrayLog.Write($"Could not set Weir to start with Windows: {ex.Message}");
            return false;
        }
    }

    /// <summary>Stops Weir starting when the person signs in.</summary>
    internal void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            TrayLog.Write(@"Weir no longer starts with Windows (HKCU\Run).");

            if (RemoveStartupShortcut())
            {
                TrayLog.Write("Removed startup folder shortcut.");
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            TrayLog.Write($"Could not stop Weir starting with Windows: {ex.Message}");
        }
    }

    /// <summary>
    /// Points the entry at the running Weir after an update, when there is one. An update never turns it on: only the person does.
    /// </summary>
    internal void RefreshIfEnabled()
    {
        if (IsEnabled)
        {
            Enable();
        }
    }

    private static string Command(string executable) => $"\"{executable}\" {Program.NoBrowserArgument}";

    private bool RemoveStartupShortcut()
    {
        var shortcut = Path.Combine(startupFolder, StartupShortcutName);
        if (!File.Exists(shortcut))
        {
            return false;
        }
        File.Delete(shortcut);
        return true;
    }
}
