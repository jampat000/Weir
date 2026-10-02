using System.Net;
using Weir.Infrastructure.Runtime;

namespace Weir.Infrastructure.Tests.Platform;

public sealed class LanAddressPickerTests
{
    private static LanAdapter Adapter(bool usable, bool gateway, params string[] addresses) =>
        new(usable, gateway, addresses.Select(IPAddress.Parse).ToList());

    private static string[] Pick(params LanAdapter[] adapters) =>
        LanAddressPicker.Pick(adapters).Select(address => address.ToString()).ToArray();

    [Fact]
    public void An_adapter_with_a_gateway_gives_its_address()
    {
        Assert.Equal(["10.1.1.196"], Pick(Adapter(usable: true, gateway: true, "10.1.1.196")));
    }

    [Fact]
    public void A_virtual_switch_without_a_gateway_is_left_out_when_a_routed_adapter_exists()
    {
        var picked = Pick(
            Adapter(usable: true, gateway: false, "172.28.80.1"),
            Adapter(usable: true, gateway: true, "10.1.1.196"));

        Assert.Equal(["10.1.1.196"], picked);
    }

    [Fact]
    public void Without_any_gateway_every_usable_adapter_is_offered()
    {
        var picked = Pick(
            Adapter(usable: true, gateway: false, "192.168.0.5"),
            Adapter(usable: true, gateway: false, "192.168.1.5"));

        Assert.Equal(["192.168.0.5", "192.168.1.5"], picked);
    }

    [Fact]
    public void An_adapter_that_is_down_is_never_offered()
    {
        Assert.Empty(Pick(Adapter(usable: false, gateway: true, "10.1.1.196")));
    }

    [Fact]
    public void A_self_assigned_address_is_never_offered()
    {
        Assert.Equal(["10.1.1.196"], Pick(Adapter(usable: true, gateway: true, "169.254.10.20", "10.1.1.196")));
    }

    [Fact]
    public void An_address_two_adapters_share_is_offered_once()
    {
        var picked = Pick(
            Adapter(usable: true, gateway: true, "10.1.1.196"),
            Adapter(usable: true, gateway: true, "10.1.1.196"));

        Assert.Equal(["10.1.1.196"], picked);
    }
}
