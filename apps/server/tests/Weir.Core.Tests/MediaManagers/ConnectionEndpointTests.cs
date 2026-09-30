using Weir.Core.MediaManagers;

namespace Weir.Core.Tests.MediaManagers;

/// <summary>Where a connection's address points, compared by host and port rather than by how it was typed.</summary>
public sealed class ConnectionEndpointTests
{
    [Theory]
    [InlineData("http://Seedbox:8080", "seedbox", 8080)]
    [InlineData("http://nas", "nas", 80)]
    [InlineData("https://sab.lan/path", "sab.lan", 443)]
    [InlineData("http://[::1]:9091", "::1", 9091)]
    [InlineData("  http://192.0.2.41:9091  ", "192.0.2.41", 9091)]
    public void An_address_names_its_host_in_lower_case_and_its_own_or_the_schemes_port(string baseUrl, string host, int port)
    {
        Assert.Equal(new ConnectionEndpoint(host, port), ConnectionEndpoint.Parse(baseUrl));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("http://nas:notaport")]
    [InlineData(null)]
    public void An_address_with_no_usable_host_or_port_has_no_endpoint(string? baseUrl)
    {
        Assert.Null(ConnectionEndpoint.Parse(baseUrl));
    }
}
