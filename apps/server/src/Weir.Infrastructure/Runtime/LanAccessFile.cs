using System.Text;

namespace Weir.Infrastructure.Runtime;

/// <summary>
/// The saved LAN access choice in Weir's data folder: <c>lan-access</c>, holding <c>on</c> (devices on the network)
/// or <c>off</c> (this PC only). The Windows tray owns the file's meaning: it reads it at every start, restarts the
/// server when it changes, and its <c>--allow-lan</c> flag writes it. This class reads and writes the very same text
/// (<c>apps/tray/Weir.Tray/LanAccess/LanAccessSetting.cs</c>), so keep the two in step.
/// </summary>
public sealed class LanAccessFile
{
    public const string FileName = "lan-access";

    private const string NetworkText = "on";
    private const string ThisPcOnlyText = "off";

    private readonly string _home;

    /// <param name="home">Weir's data folder (<c>WEIR_HOME</c>), where the tray looks for the file.</param>
    public LanAccessFile(string home)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        _home = home;
    }

    /// <summary>
    /// The saved choice, or null when none has been saved. Text the tray would not understand reads as this PC only,
    /// as it does there: opening Weir to the network is never a guess.
    /// </summary>
    public NetworkScope? Read()
    {
        var path = Path.Join(_home, FileName);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return File.ReadAllText(path).Trim() == NetworkText ? NetworkScope.Network : NetworkScope.ThisPcOnly;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return NetworkScope.ThisPcOnly;
        }
    }

    /// <summary>Saves <paramref name="scope"/>. Writing the same choice again still touches the file, which the tray takes as a request to ask for the firewall rule again.</summary>
    public void Write(NetworkScope scope) =>
        AtomicFileWriter.Replace(_home, FileName, Encoding.UTF8.GetBytes(scope == NetworkScope.Network ? NetworkText : ThisPcOnlyText));
}
