using System.Globalization;
using Weir.Tray.LanAccess;
using Xunit;

namespace Weir.Tray.Tests.LanAccess;

/// <summary>What the server is started with in each LAN access state.</summary>
public sealed class ServerListenArgumentsTests
{
    [Fact]
    public void This_pc_only_binds_localhost_which_the_server_opens_on_both_loopback_addresses()
    {
        Assert.Equal("--port 9347 --host localhost", ServerListenArguments.For(9347, ListenScope.ThisPcOnly));
    }

    [Fact]
    public void Other_devices_binds_every_interface()
    {
        Assert.Equal("--port 9347 --host 0.0.0.0", ServerListenArguments.For(9347, ListenScope.OtherDevices));
    }

    [Fact]
    public void The_port_is_written_without_a_thousands_separator_in_any_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            Assert.Equal("--port 12345 --host localhost", ServerListenArguments.For(12345, ListenScope.ThisPcOnly));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
