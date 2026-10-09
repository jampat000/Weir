using System.Security;
using Microsoft.Win32;

namespace Weir.Tray;

/// <summary>
/// Whether the person has answered "Start Weir when you sign in to Windows?", at the first-run question or by ticking or
/// unticking the tray menu's item, for this Windows user. Only the fact is kept, not the answer: the answer is whether the
/// Run entry exists. An entry with no answer behind it was set without anyone being asked, by a version that registered it at
/// install, and the tray asks about it once.
/// </summary>
sealed class StartWithWindowsAnswer(string keyPath)
{
    private const string DefaultKeyPath = @"SOFTWARE\Weir";
    private const string ValueName = "StartWithWindowsAnswered";

    /// <summary>The current user's record.</summary>
    internal static StartWithWindowsAnswer ForThisUser() => new(DefaultKeyPath);

    /// <summary>Whether the person has answered.</summary>
    internal bool IsRecorded
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(keyPath);
                return key?.GetValue(ValueName) is not null;
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                TrayLog.Write($"Could not read whether the start-with-Windows question was answered: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>Records that the person has answered.</summary>
    internal void Record()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
            key.SetValue(ValueName, 1, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            TrayLog.Write($"Could not record the answer to the start-with-Windows question: {ex.Message}");
        }
    }

    /// <summary>Forgets the answer, so a later install asks again.</summary>
    internal void Forget()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            TrayLog.Write($"Could not forget the answer to the start-with-Windows question: {ex.Message}");
        }
    }
}
