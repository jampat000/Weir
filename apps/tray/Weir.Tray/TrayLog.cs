using System.Diagnostics;
using System.Globalization;

namespace Weir.Tray;

/// <summary>
/// The tray's one log: tray-host.log in the runtime home. The UI thread, the server watchdog, the update loops and
/// the installer hooks all write through here, under one lock, so their lines never interleave.
/// </summary>
static class TrayLog
{
    internal const string FileName = "tray-host.log";

    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

    private static readonly Lock Gate = new();

    internal static void Write(string message)
    {
        var timestamp = DateTime.Now.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var line = $"[{timestamp}] {message}\n";
        lock (Gate)
        {
            try
            {
                // Resolved per write: WEIR_HOME can change under the tests, and the installer hooks run before
                // the tray has settled anything.
                var home = Program.RuntimeHome();
                Directory.CreateDirectory(home);
                File.AppendAllText(Path.Combine(home, FileName), line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // The log itself cannot be written, so the failure goes to any attached debugger or trace listener.
                Trace.WriteLine($"Weir tray could not write {FileName} ({ex.Message}): {message}");
            }
        }
    }
}
