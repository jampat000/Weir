using System.ComponentModel;
using System.Diagnostics;

namespace Weir.Tray;

/// <summary>The folders the tray menu opens in File Explorer.</summary>
static class RuntimeFolders
{
    internal const string LogFolderVariable = "WEIR_LOG_DIR";

    private const string DefaultLogFolderName = "logs";

    /// <summary>
    /// Where the server writes its logs: <c>WEIR_LOG_DIR</c> if set (taken relative to the data folder when it is not
    /// a full path), else <c>logs</c> in the data folder. The server resolves it the same way.
    /// </summary>
    internal static string Logs(string runtimeHome, Func<string, string?> getEnvironment)
    {
        var configured = getEnvironment(LogFolderVariable)?.Trim();
        if (string.IsNullOrEmpty(configured))
        {
            return Path.Combine(runtimeHome, DefaultLogFolderName);
        }
        return Path.GetFullPath(Path.IsPathFullyQualified(configured) ? configured : Path.Combine(runtimeHome, configured));
    }

    /// <summary>
    /// Opens <paramref name="folder"/> in File Explorer, creating it first if the server has not yet. When it cannot be
    /// opened, tells the person where it is so they can open it themselves.
    /// </summary>
    /// <param name="folder">The folder to open.</param>
    /// <param name="what">What the folder is, in the person's words: "data folder", "logs folder".</param>
    internal static void Open(string folder, string what)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            TrayLog.Write($"Could not open the {what} {folder}: {ex.Message}");
            MessageBox.Show(
                $"Weir could not open its {what}. You can open it in File Explorer yourself:\n\n{folder}",
                "Weir",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
