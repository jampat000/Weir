using System.Net;
using System.Net.Sockets;
using Weir.Infrastructure.Http;

namespace Weir.Infrastructure.Tests.Http;

/// <summary>
/// Ports of audit report findings H1/H2: address filtering after resolution, proved with a fake resolver rather
/// than real DNS so the PoC hostnames from the report behave the same every run.
/// </summary>
public sealed class OutboundAddressGuardTests
{
    [Theory]
    [InlineData("169.254.169.254", false)] // cloud metadata endpoint
    [InlineData("169.254.1.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)] // multicast
    [InlineData("127.0.0.1", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("8.8.8.8", true)]
    public void Local_service_addresses_allow_loopback_and_private_but_refuse_link_local_and_multicast(string address, bool allowed) =>
        Assert.Equal(allowed, OutboundAddressGuard.IsLocalServiceAddress(OutboundAddressGuard.Classify(IPAddress.Parse(address))));

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.5", false)]
    [InlineData("169.254.169.254", false)]
    public void Public_only_addresses_refuse_everything_that_is_not_globally_routable(string address, bool allowed) =>
        Assert.Equal(allowed, OutboundAddressGuard.IsPublic(OutboundAddressGuard.Classify(IPAddress.Parse(address))));

    [Fact]
    public void An_ipv4_mapped_ipv6_address_is_classified_by_its_embedded_ipv4_form()
    {
        var mappedMetadata = OutboundAddressGuard.Classify(IPAddress.Parse("::ffff:169.254.169.254"));
        Assert.False(OutboundAddressGuard.IsLocalServiceAddress(mappedMetadata));

        var mappedPrivate = OutboundAddressGuard.Classify(IPAddress.Parse("::ffff:10.0.0.5"));
        Assert.True(OutboundAddressGuard.IsLocalServiceAddress(mappedPrivate));
        Assert.False(OutboundAddressGuard.IsPublic(mappedPrivate));
    }

    [Theory]
    [InlineData("metadata.google.internal", "169.254.169.254")] // H2: a hostname resolving to the metadata endpoint
    [InlineData("localtest.me", "127.0.0.1")] // H2: public DNS name that resolves to loopback
    [InlineData("decimal-loopback.example", "127.0.0.1")] // H2: a numeric-encoded host, once resolved
    public async Task A_hostname_that_resolves_to_a_non_public_address_is_refused_for_the_public_policy(string host, string resolvesTo)
    {
        var resolved = await OutboundAddressGuard.ResolveAllowedAsync(host, OutboundAddressGuard.IsPublic, FakeResolver(resolvesTo), CancellationToken.None);

        Assert.False(resolved.CouldNotResolve);
        Assert.Empty(resolved.Allowed);
    }

    [Fact]
    public async Task A_hostname_resolving_to_a_public_address_is_allowed()
    {
        var resolved = await OutboundAddressGuard.ResolveAllowedAsync("api.example.com", OutboundAddressGuard.IsPublic, FakeResolver("93.184.216.34"), CancellationToken.None);

        Assert.False(resolved.CouldNotResolve);
        Assert.Equal(IPAddress.Parse("93.184.216.34"), Assert.Single(resolved.Allowed));
    }

    [Fact]
    public async Task An_ip_literal_is_used_directly_with_no_resolver_call()
    {
        var calls = 0;
        OutboundAddressGuard.HostResolver resolver = (_, _) =>
        {
            calls++;
            return Task.FromResult(Array.Empty<IPAddress>());
        };

        var resolved = await OutboundAddressGuard.ResolveAllowedAsync("10.0.0.5", OutboundAddressGuard.IsLocalServiceAddress, resolver, CancellationToken.None);

        Assert.Equal(0, calls);
        Assert.Equal(IPAddress.Parse("10.0.0.5"), Assert.Single(resolved.Allowed));
    }

    [Fact]
    public async Task A_lookup_failure_is_reported_distinctly_from_a_resolved_but_refused_address()
    {
        OutboundAddressGuard.HostResolver failing = (_, _) => throw new SocketException();

        var resolved = await OutboundAddressGuard.ResolveAllowedAsync("does-not-exist.example", OutboundAddressGuard.IsPublic, failing, CancellationToken.None);

        Assert.True(resolved.CouldNotResolve);
        Assert.Empty(resolved.Allowed);
    }

    private static OutboundAddressGuard.HostResolver FakeResolver(string address) =>
        (_, _) => Task.FromResult(new[] { IPAddress.Parse(address) });
}
