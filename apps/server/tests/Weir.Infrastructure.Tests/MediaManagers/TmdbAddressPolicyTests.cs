using System.Net;
using Weir.Core.Rules;
using Weir.Infrastructure.MediaManagers;

namespace Weir.Infrastructure.Tests.MediaManagers;

/// <summary>
/// The metadata provider resolves its configured host and requires a public address, the same check the
/// notification poster applies — proved with a fake resolver, never real DNS.
/// </summary>
public sealed class TmdbAddressPolicyTests
{
    [Theory]
    [InlineData("metadata.google.internal", "169.254.169.254")]
    [InlineData("localtest.me", "127.0.0.1")]
    [InlineData("decimal-encoded.example", "127.0.0.1")] // http://2130706433/ once resolved
    [InlineData("hex-encoded.example", "127.0.0.1")] // http://0x7f000001/ once resolved
    [InlineData("private-gateway.example", "10.0.0.5")]
    public async Task A_base_url_host_that_resolves_to_a_non_public_address_is_refused(string host, string resolvesTo)
    {
        using var factory = new SocketsManagerHttpHandlerFactory((_, _) => Task.FromResult(new[] { IPAddress.Parse(resolvesTo) }));
        var provider = new TmdbMetadataProvider("k", factory, $"http://{host}", new MetadataLookupCache());

        var result = await provider.LookupMovieAsync("Film", 2001);

        Assert.Equal(LookupResult.StatusUnreachable, result.Status);
        Assert.DoesNotContain(resolvesTo, result.Detail, StringComparison.Ordinal);
    }
}
