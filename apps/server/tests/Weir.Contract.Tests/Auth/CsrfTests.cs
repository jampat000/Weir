using System.Net;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Auth.AuthSupport;

namespace Weir.Contract.Tests.Auth;

/// <summary>
/// CSRF: the token endpoint needs a session secret, and browser posts are checked against the trusted origins (Origin,
/// then Referer, with localhost and 127.0.0.1 paired), seen through <c>POST /auth/login</c>.
/// </summary>
[ContractArea("auth")]
public sealed class CsrfTests(CsrfTests.CsrfServerFixture fixture) : AuthTestBase(fixture), IClassFixture<CsrfTests.CsrfServerFixture>
{
    private const string Trusted = "http://127.0.0.1:9000";
    private const string CorsOnly = "http://127.0.0.1:8782";

    private static readonly (string Name, string Value) RequestedWith = ("X-Requested-With", "XMLHttpRequest");

    [Fact]
    public async Task Auth_csrf_endpoint_503_without_session_secret()
    {
        await using var server = await WeirServer.StartNewAsync(new Dictionary<string, string> { [ServerEnvironment.SessionSecretVariable] = string.Empty });

        var response = await NewSession(server).GetAsync($"{WeirClient.Api}/auth/csrf");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
    }

    [Fact]
    public async Task Origin_skipped_when_no_trusted_list()
    {
        await using var server = await WeirServer.StartNewAsync();
        var session = NewSession(server);
        await session.EnsureAdminAccountAsync();

        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(session));
        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(session, ("Origin", "http://evil.test"), RequestedWith));
    }

    [Fact]
    public async Task Origin_enforced_when_configured()
    {
        await EnsureAdminAsync();
        var session = NewSession();

        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(session, ("Origin", Trusted), RequestedWith));
        Assert.Equal(HttpStatusCode.Forbidden, await LoginStatusAsync(session, ("Origin", "http://evil.test"), RequestedWith));
    }

    [Fact]
    public async Task Referer_checked_when_origin_absent()
    {
        await EnsureAdminAsync();
        var session = NewSession();

        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(session, ("Referer", $"{Trusted}/login")));
        Assert.Equal(HttpStatusCode.Forbidden, await LoginStatusAsync(session, ("Referer", "http://evil.test/login")));
        Assert.Equal(HttpStatusCode.Forbidden, await LoginStatusAsync(session));
    }

    [Fact]
    public async Task Origin_uses_trusted_browser_origins_override()
    {
        await EnsureAdminAsync();
        var session = NewSession();

        Assert.Equal(HttpStatusCode.Forbidden, await LoginStatusAsync(session, ("Origin", CorsOnly), RequestedWith));
    }

    /// <summary>In WEIR_ENV=development (set by this class's server), 127.0.0.1 and localhost at the same port are paired.</summary>
    [Fact]
    public async Task Expand_loopback_browser_origins_pairs_localhost()
    {
        await EnsureAdminAsync();
        var session = NewSession();

        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(session, ("Origin", "http://localhost:9000"), RequestedWith));
        Assert.Equal(HttpStatusCode.Forbidden, await LoginStatusAsync(session, ("Origin", "http://localhost:9001"), RequestedWith));
    }

    private async Task EnsureAdminAsync() =>
        await NewSession(Headers(("Origin", Trusted), RequestedWith)).EnsureAdminAccountAsync();

    private static async Task<HttpStatusCode> LoginStatusAsync(WeirClient session, params (string Name, string Value)[] headers) =>
        (await session.AttemptLoginAsync(headers: Headers(headers))).Status;

    /// <summary>
    /// WEIR_TRUSTED_BROWSER_ORIGINS overrides WEIR_CORS_ORIGINS for the Origin check. WEIR_ENV is unset (production) by
    /// default; the loopback-pairing test needs development.
    /// </summary>
    public sealed class CsrfServerFixture : ServerFixture
    {
        protected override IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string>
        {
            ["WEIR_TRUSTED_BROWSER_ORIGINS"] = Trusted,
            ["WEIR_CORS_ORIGINS"] = CorsOnly,
            ["WEIR_ENV"] = "development",
        };
    }
}
