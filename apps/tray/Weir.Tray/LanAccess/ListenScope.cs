using System.Globalization;

namespace Weir.Tray.LanAccess;

/// <summary>Who the bundled server accepts connections from.</summary>
enum ListenScope
{
    /// <summary>Only this PC: the server binds 127.0.0.1 and [::1], so nothing on the network can even connect.</summary>
    ThisPcOnly,

    /// <summary>Every device that can reach this PC, subject to Windows Firewall.</summary>
    OtherDevices,
}

/// <summary>How a <see cref="ListenScope"/> reads in the log.</summary>
static class ListenScopeText
{
    internal static string Describe(this ListenScope scope) =>
        scope == ListenScope.OtherDevices ? "other devices on the network can connect" : "only this PC can connect";
}

/// <summary>The command-line arguments that make the server listen the way a <see cref="ListenScope"/> says.</summary>
static class ServerListenArguments
{
    // Weir.Host binds "localhost" to both loopback addresses and "0.0.0.0" to every interface.
    private const string ThisPcHost = "localhost";
    private const string EveryInterfaceHost = "0.0.0.0";

    internal static string For(int port, ListenScope scope) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"--port {port} --host {(scope == ListenScope.ThisPcOnly ? ThisPcHost : EveryInterfaceHost)}");
}
