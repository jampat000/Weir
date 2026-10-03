namespace Weir.Tray.LanAccess;

/// <summary>
/// The saved "LAN access" choice in the runtime home: whether the server listens for other devices on the network
/// or only for this PC. The tray reads it at every start, and <c>--allow-lan</c> writes it from another process.
/// </summary>
static class LanAccessSetting
{
    internal const string FileName = "lan-access";

    private const string OtherDevicesText = "on";
    private const string ThisPcOnlyText = "off";

    /// <summary>
    /// The saved choice, or null when nothing has been saved yet. A file that cannot be read or understood reads as
    /// <see cref="ListenScope.ThisPcOnly"/>, and <paramref name="log"/> says why: opening Weir to the network
    /// is never a guess.
    /// </summary>
    internal static ListenScope? Read(string runtimeHome, Action<string> log)
    {
        var path = Path.Combine(runtimeHome, FileName);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            var text = File.ReadAllText(path).Trim();
            if (text is OtherDevicesText)
            {
                return ListenScope.OtherDevices;
            }
            if (text is not ThisPcOnlyText)
            {
                log($"{FileName} says '{text}', which Weir does not recognise; treating LAN access as off.");
            }
            return ListenScope.ThisPcOnly;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"{FileName} could not be read ({ex.Message}); treating LAN access as off.");
            return ListenScope.ThisPcOnly;
        }
    }

    /// <summary>
    /// When the choice was last written, or null when nothing is saved. Saving the same choice again moves it, which is
    /// how a person asking again is told apart from a choice that was already there.
    /// </summary>
    internal static DateTime? SavedAt(string runtimeHome)
    {
        var path = Path.Combine(runtimeHome, FileName);
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static void Write(string runtimeHome, ListenScope scope) =>
        AtomicFile.WriteAllText(runtimeHome, FileName, scope == ListenScope.OtherDevices ? OtherDevicesText : ThisPcOnlyText);
}
