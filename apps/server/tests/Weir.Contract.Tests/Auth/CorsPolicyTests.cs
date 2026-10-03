using System.Net;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Auth.AuthSupport;

namespace Weir.Contract.Tests.Auth;

/// <summary>CORS preflight: only the methods and headers the Weir web app needs are allowed.</summary>
[ContractArea("auth")]
public sealed class CorsPolicyTests(CorsPolicyTests.CorsServerFixture fixture) : AuthTestBase(fixture), IClassFixture<CorsPolicyTests.CorsServerFixture>
{
    private const string Origin = "http://localhost:5173";
    private const string Csrf = $"{WeirClient.Api}/auth/csrf";

    [Fact]
    public async Task Cors_preflight_allows_weir_browser_methods_and_headers()
    {
        var response = await NewSession().OptionsAsync(
            Csrf,
            Headers(
                ("Origin", Origin),
                ("Access-Control-Request-Method", "POST"),
                ("Access-Control-Request-Headers", "Content-Type, X-CSRF-Token")));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var methods = response.Header("access-control-allow-methods")!;
        var headers = response.Header("access-control-allow-headers")!;
        Assert.Contains("GET", methods, StringComparison.Ordinal);
        Assert.Contains("POST", methods, StringComparison.Ordinal);
        Assert.Contains("OPTIONS", methods, StringComparison.Ordinal);
        Assert.Contains("content-type", headers.ToLowerInvariant(), StringComparison.Ordinal);
        Assert.Contains("x-csrf-token", headers.ToLowerInvariant(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cors_preflight_rejects_unneeded_methods()
    {
        var session = NewSession();
        var trace = await session.OptionsAsync(Csrf, Headers(("Origin", Origin), ("Access-Control-Request-Method", "TRACE")));
        var connect = await session.OptionsAsync(Csrf, Headers(("Origin", Origin), ("Access-Control-Request-Method", "CONNECT")));

        Assert.Equal(HttpStatusCode.BadRequest, trace.Status);
        Assert.Equal(HttpStatusCode.BadRequest, connect.Status);
        Assert.DoesNotContain("TRACE", trace.Header("access-control-allow-methods") ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("CONNECT", connect.Header("access-control-allow-methods") ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cors_preflight_rejects_unneeded_headers()
    {
        var response = await NewSession().OptionsAsync(
            Csrf,
            Headers(
                ("Origin", Origin),
                ("Access-Control-Request-Method", "POST"),
                ("Access-Control-Request-Headers", "X-Injected-Header")));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
    }

    public sealed class CorsServerFixture : ServerFixture
    {
        protected override IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string> { ["WEIR_CORS_ORIGINS"] = Origin };
    }
}
