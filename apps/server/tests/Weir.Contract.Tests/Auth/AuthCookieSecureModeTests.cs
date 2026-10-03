using System.Net;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Auth.AuthSupport;

namespace Weir.Contract.Tests.Auth;

/// <summary>
/// The sign-in cookie's <c>Secure</c> flag follows the request scheme (#452): a Secure cookie over plain HTTP is
/// silently discarded by browsers, locking the operator out.
/// </summary>
[ContractArea("auth")]
public sealed class AuthCookieSecureModeTests(ServerFixture fixture) : AliceTestBase(fixture), IClassFixture<ServerFixture>
{
    [Fact]
    public async Task Plain_http_login_does_not_set_secure_cookie()
    {
        var response = await Alice.LoginAsync();
        AssertStatus(HttpStatusCode.OK, response);

        var setCookie = SetCookieHeader(response);
        Assert.False(string.IsNullOrEmpty(setCookie), "login must set a session cookie");
        Assert.DoesNotContain("secure", setCookie.ToLowerInvariant(), StringComparison.Ordinal);
        Assert.Contains("httponly", setCookie.ToLowerInvariant(), StringComparison.Ordinal);

        AssertStatus(HttpStatusCode.OK, await Alice.GetAsync($"{AuthSession.Api}/auth/me"));
    }

    [Fact]
    public async Task Forcing_always_still_sets_secure_over_plain_http()
    {
        await using var server = await WeirServer.StartNewAsync(new Dictionary<string, string> { ["WEIR_SESSION_COOKIE_SECURE"] = "always" });
        var session = NewSession(server);
        await session.EnsureAdminAccountAsync();

        var response = await session.LoginAsync();

        AssertStatus(HttpStatusCode.OK, response);
        Assert.Contains("secure", SetCookieHeader(response).ToLowerInvariant(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Forcing_never_does_not_set_secure_even_over_https()
    {
        await using var server = await WeirServer.StartNewAsync(new Dictionary<string, string>
        {
            ["WEIR_SESSION_COOKIE_SECURE"] = "never",
            ["WEIR_TRUSTED_PROXY_IPS"] = "127.0.0.1/32",
        });
        var session = NewSession(server);
        await session.EnsureAdminAccountAsync();

        var response = await session.LoginAsync(headers: Headers(("X-Forwarded-Proto", "https")));

        AssertStatus(HttpStatusCode.OK, response);
        Assert.DoesNotContain("secure", SetCookieHeader(response).ToLowerInvariant(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logout_clears_the_cookie_it_set()
    {
        AssertStatus(HttpStatusCode.OK, await Alice.LoginAsync());

        var logout = await Alice.LogoutWithBodyAsync();
        Assert.True(logout.Status is HttpStatusCode.OK or HttpStatusCode.NoContent, logout.ToString());

        var cleared = SetCookieHeader(logout);
        Assert.False(string.IsNullOrEmpty(cleared), "logout must clear the session cookie");
        Assert.DoesNotContain("secure", cleared.ToLowerInvariant(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Alice.GetAsync($"{AuthSession.Api}/auth/me")).Status);
    }
}
