using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Auth.AuthSupport;

namespace Weir.Contract.Tests.Auth;

/// <summary>Sign-in and sign-out, password changes, CSRF on auth routes, the admin check and the login rate limit.</summary>
[ContractArea("auth")]
public sealed class AuthApiTests(ServerFixture fixture) : AliceTestBase(fixture), IClassFixture<ServerFixture>
{
    private const string Api = WeirClient.Api;
    private const string NewPassword = "new-password-stronger-123";

    [Fact]
    public async Task Login_me_logout_flow()
    {
        var login = await Alice.AttemptLoginAsync();
        AssertStatus(HttpStatusCode.OK, login);
        Assert.Equal("alice", (string)login.Fields["user"]!["username"]!);
        var cookie = Alice.Cookie(AuthSupport.SessionCookie);
        Assert.True(cookie is not null && cookie.Length > 20);

        var me = await Alice.GetAsync($"{Api}/auth/me");
        AssertStatus(HttpStatusCode.OK, me);
        Assert.Equal("alice", (string)me.Fields["user"]!["username"]!);
        var session = await Alice.GetAsync($"{Api}/auth/session");
        AssertStatus(HttpStatusCode.OK, session);
        Assert.False((bool)session.Fields["trusted_device"]!);

        AssertStatus(HttpStatusCode.NoContent, await Alice.LogoutWithHeaderAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, (await Alice.GetAsync($"{Api}/auth/me")).Status);
    }

    [Fact]
    public async Task Login_invalid_password()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Alice.AttemptLoginAsync(password: "wrong-password")).Status);
    }

    [Fact]
    public async Task Login_rejects_unexpected_fields()
    {
        var response = await Alice.AttemptLoginAsync(extra: ("unexpected", JsonValue.Create("value")));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.Status);
    }

    [Fact]
    public async Task Change_password_requires_current_and_forces_new_login()
    {
        await using var server = await WeirServer.StartNewAsync();
        var session = NewSession(server);
        await session.EnsureAdminAccountAsync();
        AssertStatus(HttpStatusCode.OK, await session.AttemptLoginAsync());

        var change = await session.PostWithCsrfAsync(
            $"{Api}/auth/change-password",
            new JsonObject { ["current_password"] = WeirClient.AdminPassword, ["new_password"] = NewPassword });

        AssertStatus(HttpStatusCode.OK, change);
        Assert.Contains("sign in again", ((string)change.Fields["message"]!).ToLowerInvariant(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync($"{Api}/auth/me")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.AttemptLoginAsync()).Status);
        AssertStatus(HttpStatusCode.OK, await session.AttemptLoginAsync(password: NewPassword));
    }

    [Fact]
    public async Task Change_password_rejects_wrong_current_password()
    {
        AssertStatus(HttpStatusCode.OK, await Alice.AttemptLoginAsync());

        var change = await Alice.PostWithCsrfAsync(
            $"{Api}/auth/change-password",
            new JsonObject { ["current_password"] = "wrong-current-password", ["new_password"] = NewPassword });

        Assert.Equal(HttpStatusCode.BadRequest, change.Status);
        Assert.Contains("current password", ((string?)change.Fields["detail"] ?? string.Empty).ToLowerInvariant(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_invalid_csrf()
    {
        var response = await Alice.PostAsync(
            $"{Api}/auth/login",
            new JsonObject
            {
                ["username"] = WeirClient.AdminUsername,
                ["password"] = WeirClient.AdminPassword,
                ["csrf_token"] = "invalid-token",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
    }

    [Fact]
    public async Task Logout_rejects_missing_csrf()
    {
        await Alice.AttemptLoginAsync();

        var response = await Alice.PostAsync($"{Api}/auth/logout");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
    }

    [Fact]
    public async Task Authenticated_csrf_token_is_rejected_across_sessions()
    {
        var clientA = NewSession();
        var clientB = NewSession();
        Assert.Equal(HttpStatusCode.OK, (await clientA.AttemptLoginAsync()).Status);
        Assert.Equal(HttpStatusCode.OK, (await clientB.AttemptLoginAsync()).Status);

        var sessionACsrf = await clientA.CsrfTokenAsync();
        var crossSession = await clientB.PostAsync(
            $"{Api}/auth/change-password",
            new JsonObject
            {
                ["csrf_token"] = sessionACsrf,
                ["current_password"] = WeirClient.AdminPassword,
                ["new_password"] = NewPassword,
            });

        Assert.Equal(HttpStatusCode.BadRequest, crossSession.Status);
    }

    [Fact]
    public async Task Admin_ping_requires_admin()
    {
        await Alice.AttemptLoginAsync();

        var response = await Alice.GetAsync($"{Api}/auth/admin/ping");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.True(JsonNode.DeepEquals(new JsonObject { ["ok"] = true }, response.Json));
    }

    [Fact]
    public async Task Admin_ping_forbidden_for_viewer()
    {
        await EnsureViewerAsync(Server);
        var session = NewSession();
        await session.AttemptLoginAsync(ViewerUsername, ViewerPassword);

        var response = await session.GetAsync($"{Api}/auth/admin/ping");

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
    }

    [Fact]
    public async Task Login_rate_limited()
    {
        await using var server = await WeirServer.StartNewAsync(new Dictionary<string, string>
        {
            ["WEIR_AUTH_LOGIN_RATE_MAX_ATTEMPTS"] = "3",
            ["WEIR_AUTH_LOGIN_RATE_WINDOW_SECONDS"] = "120",
        });
        var session = NewSession(server);
        await session.EnsureAdminAccountAsync();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            AssertStatus(HttpStatusCode.Unauthorized, await session.AttemptLoginAsync(password: "wrong"));
        }

        var limited = await session.AttemptLoginAsync(password: "wrong");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.Status);
        Assert.NotNull(limited.Header("Retry-After"));
    }
}
