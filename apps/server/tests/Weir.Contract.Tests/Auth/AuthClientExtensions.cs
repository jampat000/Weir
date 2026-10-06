using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Auth;

/// <summary>What the auth tests do with a <see cref="WeirClient"/> that no other area needs: logging out two ways and creating the first admin.</summary>
public static class AuthClientExtensions
{
    /// <summary>Logs out with the token in the JSON body.</summary>
    public static async Task<WeirResponse> LogoutWithBodyAsync(this WeirClient client) =>
        await client.PostAsync($"{WeirClient.Api}/auth/logout", new JsonObject { ["csrf_token"] = await client.CsrfTokenAsync() });

    /// <summary>Logs out with the token in the <c>X-CSRF-Token</c> header.</summary>
    public static async Task<WeirResponse> LogoutWithHeaderAsync(this WeirClient client) =>
        await client.PostAsync(
            $"{WeirClient.Api}/auth/logout",
            body: null,
            new Dictionary<string, string> { ["X-CSRF-Token"] = await client.CsrfTokenAsync() });

    /// <summary>
    /// Creates the first admin through bootstrap when the install has none, leaving this client anonymous. Bootstrap signs
    /// the new admin in, so it leaves one working session behind; its id (dashes removed, as <c>user_sessions.id</c> is
    /// stored) is returned for a test that counts sessions, or null when an admin already existed. The cookie is read from
    /// bootstrap's own <c>Set-Cookie</c> and sent explicitly, so this works when <c>Secure</c> keeps the jar from sending it.
    /// </summary>
    public static async Task<string?> EnsureAdminAccountAsync(this WeirClient client)
    {
        var status = await client.GetAsync($"{WeirClient.Api}/auth/bootstrap/status");
        Assert.True(status.Status == HttpStatusCode.OK, status.ToString());
        if (!(bool)status.Fields["bootstrap_allowed"]!)
        {
            return null;
        }

        var created = await client.BootstrapAsync();
        Assert.True(created.Status == HttpStatusCode.OK, created.ToString());
        var rawCookie = created.SetCookieValue(AuthSupport.SessionCookie);
        Assert.False(string.IsNullOrEmpty(rawCookie), "bootstrap must set a session cookie");

        var session = await client.GetAsync(
            $"{WeirClient.Api}/auth/session",
            new Dictionary<string, string> { ["Cookie"] = $"{AuthSupport.SessionCookie}={rawCookie}" });
        Assert.True(session.Status == HttpStatusCode.OK, session.ToString());
        var sessionId = ((string)session.Fields["session_id"]!).Replace("-", string.Empty, StringComparison.Ordinal);
        client.ClearCookies();
        return sessionId;
    }
}
