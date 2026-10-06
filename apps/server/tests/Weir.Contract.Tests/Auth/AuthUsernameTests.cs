using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Auth.AuthSupport;

namespace Weir.Contract.Tests.Auth;

/// <summary>Usernames are case-insensitive and can be renamed (#455).</summary>
[ContractArea("auth")]
public sealed class AuthUsernameTests(ServerFixture fixture) : AliceTestBase(fixture), IClassFixture<ServerFixture>
{
    private const string Api = WeirClient.Api;

    /// <summary>`alice` is seeded; `Alice` and `ALICE` are the same account, not a wrong password.</summary>
    [Fact]
    public async Task Login_ignores_capitalisation()
    {
        foreach (var spelling in new[] { "alice", "Alice", "ALICE", "  AlIcE  " })
        {
            var response = await Alice.AttemptLoginAsync(spelling);

            Assert.True(response.Status == HttpStatusCode.OK, $"'{spelling}': {response}");
            await Alice.LogoutWithBodyAsync();
        }
    }

    [Fact]
    public async Task A_wrong_password_is_still_rejected()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Alice.AttemptLoginAsync("ALICE", "not-the-password")).Status);
    }

    [Fact]
    public async Task Username_can_be_changed()
    {
        await using var server = await WeirServer.StartNewAsync();
        var session = NewSession(server);
        await session.EnsureAdminAccountAsync();
        Assert.Equal(HttpStatusCode.OK, (await session.AttemptLoginAsync("alice")).Status);

        var response = await session.PostWithCsrfAsync(
            $"{Api}/auth/change-username",
            new JsonObject { ["current_password"] = WeirClient.AdminPassword, ["new_username"] = "operator2" });
        AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("operator2", (string)response.Fields["username"]!);

        // The session survives a rename: its authority is the token, not the name.
        Assert.Equal("operator2", (string)(await session.GetAsync($"{Api}/auth/me")).Fields["user"]!["username"]!);

        await session.LogoutWithBodyAsync();
        Assert.Equal(HttpStatusCode.OK, (await session.AttemptLoginAsync("OPERATOR2")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.AttemptLoginAsync("alice")).Status);
    }

    /// <summary>The username is half of the credentials, so an unattended browser is not enough.</summary>
    [Fact]
    public async Task Changing_the_username_needs_the_current_password()
    {
        Assert.Equal(HttpStatusCode.OK, (await Alice.AttemptLoginAsync("alice")).Status);

        var response = await Alice.PostWithCsrfAsync(
            $"{Api}/auth/change-username",
            new JsonObject { ["current_password"] = "wrong-password", ["new_username"] = "operator2" });

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("alice", (string)(await Alice.GetAsync($"{Api}/auth/me")).Fields["user"]!["username"]!);
    }

    /// <summary>`alice` -> `Alice` collides with nobody: it is the same row, tidying its own display.</summary>
    [Fact]
    public async Task Fixing_your_own_capitalisation_is_allowed()
    {
        await using var server = await WeirServer.StartNewAsync();
        var session = NewSession(server);
        await session.EnsureAdminAccountAsync();
        Assert.Equal(HttpStatusCode.OK, (await session.AttemptLoginAsync("alice")).Status);

        var response = await session.PostWithCsrfAsync(
            $"{Api}/auth/change-username",
            new JsonObject { ["current_password"] = WeirClient.AdminPassword, ["new_username"] = "Alice" });
        AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("Alice", (string)(await session.GetAsync($"{Api}/auth/me")).Fields["user"]!["username"]!);

        await session.LogoutWithBodyAsync();
        Assert.Equal(HttpStatusCode.OK, (await session.AttemptLoginAsync("alice")).Status);
    }

    [Fact]
    public async Task An_unchanged_username_is_refused()
    {
        Assert.Equal(HttpStatusCode.OK, (await Alice.AttemptLoginAsync("alice")).Status);

        var response = await Alice.PostWithCsrfAsync(
            $"{Api}/auth/change-username",
            new JsonObject { ["current_password"] = WeirClient.AdminPassword, ["new_username"] = "alice" });

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
    }

    [Fact]
    public async Task Signed_out_callers_cannot_rename_anyone()
    {
        var response = await Alice.PostWithCsrfAsync(
            $"{Api}/auth/change-username",
            new JsonObject { ["current_password"] = WeirClient.AdminPassword, ["new_username"] = "operator2" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
    }
}
