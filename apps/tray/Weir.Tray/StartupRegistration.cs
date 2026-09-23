using Microsoft.Win32;

namespace Weir.Tray;

/// <summary>
/// Starts Weir at sign-in through the current user's Run key. The install and update hooks call it, and a
/// sign-in start passes <see cref="Program.NoBrowserArgument"/> because nobody asked to see Weir then (#638).
/// </summary>
static class StartupRegistration
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Weir";
    private const string StartupShortcutName = "Weir.lnk";

    // Both hooks run inside the installer. A registry or file error there is logged, never thrown: failing an
    // install or uninstall over a sign-in shortcut would leave Weir half-installed.
    internal static void Register()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                return;
            }
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.SetValue(ValueName, $"\"{exe}\" {Program.NoBrowserArgument}");
            TrayLog.Write(@"Registered Weir startup (HKCU\Run).");

            // A startup-folder shortcut made by hand would start a second copy at sign-in; the Run key is the one entry.
            if (RemoveStartupShortcut())
            {
                TrayLog.Write("Removed the startup folder shortcut; the Run key starts Weir now.");
            }
        }
        catch (Exception ex)
        {
            TrayLog.Write($"Could not register startup: {ex.Message}");
        }
    }

    internal static void Deregister()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            TrayLog.Write(@"Deregistered Weir startup (HKCU\Run).");

            if (RemoveStartupShortcut())
            {
                TrayLog.Write("Removed startup folder shortcut.");
            }
        }
        catch (Exception ex)
        {
            TrayLog.Write($"Could not deregister startup: {ex.Message}");
        }
    }

    private static bool RemoveStartupShortcut()
    {
        var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupShortcutName);
        if (!File.Exists(shortcut))
        {
            return false;
        }
        File.Delete(shortcut);
        return true;
    }
}
