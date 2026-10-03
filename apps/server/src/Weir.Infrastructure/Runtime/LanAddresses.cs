using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Weir.Infrastructure.Runtime;

/// <summary>One network adapter, as much of it as choosing the addresses other devices would use needs.</summary>
internal readonly record struct LanAdapter(bool IsUsable, bool HasGateway, IReadOnlyList<IPAddress> Ipv4Addresses);

/// <summary>
/// Picks the IPv4 addresses other devices on the network would type to reach this PC. Adapters with a gateway are
/// the ones that lead somewhere (a router): virtual switches such as WSL's or Hyper-V's have none, so they are left
/// out unless nothing else is left. Self-assigned 169.254.x.x addresses are never offered.
/// </summary>
internal static class LanAddressPicker
{
    private const byte LinkLocalFirstOctet = 169;
    private const byte LinkLocalSecondOctet = 254;

    internal static IReadOnlyList<IPAddress> Pick(IEnumerable<LanAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        var usable = adapters.Where(adapter => adapter.IsUsable).ToList();
        var routed = usable.Where(adapter => adapter.HasGateway).ToList();
        return (routed.Count > 0 ? routed : usable)
            .SelectMany(adapter => adapter.Ipv4Addresses)
            .Where(address => !IsLinkLocal(address))
            .Distinct()
            .ToList();
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var octets = address.GetAddressBytes();
        return octets[0] == LinkLocalFirstOctet && octets[1] == LinkLocalSecondOctet;
    }
}

/// <summary>The addresses of this PC's network adapters, as the operating system reports them.</summary>
public sealed class NetworkInterfaceLanAddresses : ILanAddresses
{
    public IReadOnlyList<string> Read() =>
        LanAddressPicker.Pick(NetworkInterface.GetAllNetworkInterfaces().Select(Describe))
            .Select(address => address.ToString())
            .ToList();

    private static LanAdapter Describe(NetworkInterface adapter)
    {
        var usable = adapter.OperationalStatus == OperationalStatus.Up
            && adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel);
        if (!usable)
        {
            return new LanAdapter(IsUsable: false, HasGateway: false, []);
        }

        var properties = adapter.GetIPProperties();
        var hasGateway = properties.GatewayAddresses.Any(gateway =>
            gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any));
        var ipv4 = properties.UnicastAddresses
            .Select(unicast => unicast.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
            .ToList();
        return new LanAdapter(IsUsable: true, hasGateway, ipv4);
    }
}
