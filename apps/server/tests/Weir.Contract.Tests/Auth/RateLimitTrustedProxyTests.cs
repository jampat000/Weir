using System.Net;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Auth.AuthSupport;

namespace Weir.Contract.Tests.Auth;

/// <summary>
/// Sign-in rate limiting behind a trusted proxy. With <c>WEIR_TRUSTED_PROXY_IPS</c> covering the peer, the login rate
/// limit is keyed on the rightmost untrusted <c>X-Forwarded-For</c> address, so clients behind one proxy do not share a bucket.
/// </summary>
[ContractArea("auth")]
public sealed class RateLimitTrustedProxyTests(RateLimitTrustedProxyTests.ProxyRateLimitServerFixture fixture)
    : AuthTestBase(fixture), IClassFixture<RateLimitTrustedProxyTests.ProxyRateLimitServerFixture>
{
    [Fact]
    public async Task Rate_limit_uses_forwarded_for_from_trusted_proxy()
    {
        var session = NewSession();

        Assert.Equal(HttpStatusCode.Unauthorized, await AttemptAsync(session, "203.0.113.10"));
        Assert.Equal(HttpStatusCode.Unauthorized, await AttemptAsync(session, "203.0.113.10"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await AttemptAsync(session, "203.0.113.10"));
        // Another client behind the same proxy has its own window.
        Assert.Equal(HttpStatusCode.Unauthorized, await AttemptAsync(session, "203.0.113.11"));
    }

    [Fact]
    public async Task Rate_limit_uses_rightmost_untrusted_forwarded_for_from_trusted_proxy()
    {
        var session = NewSession();

        Assert.Equal(HttpStatusCode.Unauthorized, await AttemptAsync(session, "203.0.113.7"));
        Assert.Equal(HttpStatusCode.Unauthorized, await AttemptAsync(session, "203.0.113.7"));
        // The client-supplied left part of the chain does not escape the bucket of the address the proxy saw.
        Assert.Equal(HttpStatusCode.TooManyRequests, await AttemptAsync(session, "198.51.100.20, 203.0.113.7"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await AttemptAsync(session, "198.51.100.20, 203.0.113.7, 127.0.0.1"));
        // A different rightmost address is a different client.
        Assert.Equal(HttpStatusCode.Unauthorized, await AttemptAsync(session, "203.0.113.7, 198.51.100.20"));
    }

    private static async Task<HttpStatusCode> AttemptAsync(WeirClient session, string forwardedFor) =>
        (await session.AttemptLoginAsync(password: "wrong", headers: Headers(("X-Forwarded-For", forwardedFor)))).Status;

    public sealed class ProxyRateLimitServerFixture : ServerFixture
    {
        protected override IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string>
        {
            ["WEIR_TRUSTED_PROXY_IPS"] = "127.0.0.1/32",
            ["WEIR_AUTH_LOGIN_RATE_MAX_ATTEMPTS"] = "2",
            ["WEIR_AUTH_LOGIN_RATE_WINDOW_SECONDS"] = "3600",
        };
    }
}
