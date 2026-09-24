using Weir.Core.Net;

namespace Weir.Core.Tests.Net;

/// <summary>The Host allow-list rule, independent of ASP.NET (see the middleware integration tests for the pipeline).</summary>
public sealed class AllowedHostPolicyTests
{
    private static readonly string[] NoAllowedHosts = [];
    private static readonly string[] NoTrustedOrigins = [];

    [Theory]
    [InlineData("192.0.2.10")]
    [InlineData("192.0.2.10:9347")]
    [InlineData("[::1]")]
    [InlineData("[::1]:9347")]
    [InlineData("localhost")]
    [InlineData("LOCALHOST:9347")]
    [InlineData("weir")]
    [InlineData("weir-box.local")]
    [InlineData("weir-box.lan")]
    [InlineData("weir-box.home")]
    [InlineData("nas.home.arpa")]
    [InlineData("weir-box.internal")]
    [InlineData("weir-box.localdomain")]
    public void An_ip_literal_localhost_single_label_or_local_network_name_is_allowed(string host) =>
        Assert.True(AllowedHostPolicy.IsAllowed(host, NoAllowedHosts, NoTrustedOrigins));

    [Theory]
    [InlineData("evil.example")]
    [InlineData("evil.example:9347")]
    [InlineData("weir.attacker.example")]
    public void An_unrecognised_public_domain_is_refused(string host) =>
        Assert.False(AllowedHostPolicy.IsAllowed(host, NoAllowedHosts, NoTrustedOrigins));

    [Fact]
    public void An_entry_in_allowed_hosts_matches_exactly_but_not_a_subdomain()
    {
        string[] allowed = ["weir.example"];
        Assert.True(AllowedHostPolicy.IsAllowed("weir.example:9347", allowed, NoTrustedOrigins));
        Assert.False(AllowedHostPolicy.IsAllowed("other.weir.example", allowed, NoTrustedOrigins));
    }

    [Fact]
    public void A_wildcard_allowed_hosts_entry_matches_only_its_subdomains()
    {
        string[] allowed = ["*.weir.example"];
        Assert.True(AllowedHostPolicy.IsAllowed("nas.weir.example", allowed, NoTrustedOrigins));
        Assert.False(AllowedHostPolicy.IsAllowed("weir.example", allowed, NoTrustedOrigins));
    }

    [Fact]
    public void A_trusted_browser_origins_host_is_allowed()
    {
        string[] trusted = ["https://weir.example:8443"];
        Assert.True(AllowedHostPolicy.IsAllowed("weir.example:9347", NoAllowedHosts, trusted));
        Assert.False(AllowedHostPolicy.IsAllowed("other.example", NoAllowedHosts, trusted));
    }

    [Fact]
    public void An_empty_host_is_refused() => Assert.False(AllowedHostPolicy.IsAllowed(string.Empty, NoAllowedHosts, NoTrustedOrigins));
}
