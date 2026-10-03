using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Auth.AuthSupport;

namespace Weir.Contract.Tests.Auth;

/// <summary>Sign-in sessions: the cookie, trusted devices, restarts, the five-session limit, expiry and cleanup.</summary>
[ContractArea("auth")]
public sealed class AuthSessionsTests(ServerFixture fixture) : AliceTestBase(fixture), IClassFixture<ServerFixture>
{
    private const string Api = WeirClient.Api;

    // Sessions that are already past their absolute expiry are deleted at server start, so a test that needs one alive at
    // startup and expired at request time seeds it to expire shortly after the restart.
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(30);

    private static readonly Dictionary<string, string> IdleMinutes720 = new() { ["WEIR_SESSION_IDLE_MINUTES"] = "720" };

    [Fact]
    public async Task Session_rotation_replaces_old_cookie()
    {
        await Alice.AttemptLoginAsync();
        var oldCookie = Alice.Cookie(AuthSupport.SessionCookie);
        await Alice.AttemptLoginAsync();
        var newCookie = Alice.Cookie(AuthSupport.SessionCookie);

        Assert.NotEqual(oldCookie, newCookie);
        Assert.Equal(HttpStatusCode.OK, (await Alice.GetAsync($"{Api}/auth/me")).Status);
    }

    [Fact]
    public async Task Login_cookie_has_explicit_lifetime()
    {
        var response = await Alice.AttemptLoginAsync();

        AssertStatus(HttpStatusCode.OK, response);
        var setCookie = SetCookieHeader(response);
        Assert.Contains("Max-Age=", setCookie, StringComparison.Ordinal);
        Assert.Contains("HttpOnly", setCookie, StringComparison.Ordinal);
        Assert.Contains("SameSite=", setCookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Trusted_device_login_uses_extended_session_policy()
    {
        var response = await Alice.AttemptLoginAsync(extra: ("trusted_device", JsonValue.Create(true)));
        AssertStatus(HttpStatusCode.OK, response);

        var sessionResponse = await Alice.GetAsync($"{Api}/auth/session");
        AssertStatus(HttpStatusCode.OK, sessionResponse);
        var body = sessionResponse.Fields;
        Assert.True((bool)body["trusted_device"]!);
        Assert.Equal(60 * 1440, (int)body["idle_timeout_minutes"]!);
        Assert.Equal(365, (int)body["absolute_timeout_days"]!);
        var sessionId = (string)body["session_id"]!;

        List<Dictionary<string, object?>> newest;
        await using (var database = await Server.StopForDatabaseAsync())
        {
            newest = SeedSql.Rows(database.Connection, "SELECT id, is_trusted_device FROM user_sessions ORDER BY created_at DESC LIMIT 1");
        }

        Assert.NotEmpty(newest);
        Assert.Equal(1L, newest[0]["is_trusted_device"]);
        Assert.Equal(sessionId.Replace("-", string.Empty, StringComparison.Ordinal), ((string)newest[0]["id"]!).Replace("-", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Session_cookie_survives_backend_app_restart()
    {
        var login = await Alice.AttemptLoginAsync();
        AssertStatus(HttpStatusCode.OK, login);
        var cookie = Alice.Cookie(AuthSupport.SessionCookie);
        Assert.False(string.IsNullOrEmpty(cookie));

        await Server.RestartAsync();

        var after = NewSession();
        after.SetCookie(AuthSupport.SessionCookie, cookie);
        var me = await after.GetAsync($"{Api}/auth/me");
        AssertStatus(HttpStatusCode.OK, me);
        Assert.Equal("alice", (string)me.Fields["user"]!["username"]!);
    }

    [Fact]
    public async Task Second_browser_login_does_not_revoke_first_browser_session()
    {
        var remoteClient = NewSession();
        var localClient = NewSession();
        Assert.Equal(HttpStatusCode.OK, (await remoteClient.AttemptLoginAsync()).Status);
        Assert.Equal(HttpStatusCode.OK, (await remoteClient.GetAsync($"{Api}/auth/me")).Status);

        Assert.Equal(HttpStatusCode.OK, (await localClient.AttemptLoginAsync()).Status);
        Assert.Equal(HttpStatusCode.OK, (await localClient.GetAsync($"{Api}/auth/me")).Status);
        Assert.Equal(HttpStatusCode.OK, (await remoteClient.GetAsync($"{Api}/auth/me")).Status);
    }

    [Fact]
    public async Task Login_keeps_only_newest_five_active_sessions()
    {
        await using var server = await WeirServer.StartNewAsync();
        // The admin's own bootstrap sign-in is itself a session, so it is one of the seven vying for the newest-5 slots
        // below: it and the oldest of the six logins get revoked.
        var bootstrapSessionId = await NewSession(server).EnsureAdminAccountAsync();
        var clients = Enumerable.Range(0, 6).Select(_ => NewSession(server)).ToList();
        foreach (var client in clients)
        {
            AssertStatus(HttpStatusCode.OK, await client.AttemptLoginAsync());
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await clients[0].GetAsync($"{Api}/auth/me")).Status);
        foreach (var client in clients.Skip(1))
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Api}/auth/me")).Status);
        }

        long active;
        List<string> revokedIds;
        await using (var database = await StopForInspectionAsync(server))
        {
            active = Convert.ToInt64(SeedSql.Scalar(database.Connection, "SELECT COUNT(*) FROM user_sessions WHERE revoked_at IS NULL"), System.Globalization.CultureInfo.InvariantCulture);
            revokedIds = SeedSql.Rows(database.Connection, "SELECT id FROM user_sessions WHERE revoked_at IS NOT NULL").Select(row => (string)row["id"]!).ToList();
        }

        Assert.Equal(5, active);
        Assert.Equal(2, revokedIds.Count);
        Assert.Contains(bootstrapSessionId, revokedIds);
    }

    [Fact]
    public async Task Session_limit_ignores_absolute_expired_sessions()
    {
        await using var server = await WeirServer.StartNewAsync();
        var bootstrapSessionId = await NewSession(server).EnsureAdminAccountAsync();
        DateTime expires;
        List<string> expiredIds = [];
        await using (var database = await server.StopForDatabaseAsync())
        {
            var now = UtcNow();
            expires = now + ExpiryMargin;
            var userId = UserId(database.Connection, WeirClient.AdminUsername);
            for (var i = 0; i < 5; i++)
            {
                expiredIds.Add(InsertSession(database.Connection, userId, now - TimeSpan.FromMinutes(10 - i), expires, now).Id);
            }
        }

        await WaitPastAsync(expires);

        var live = NewSession(server);
        Assert.Equal(HttpStatusCode.OK, (await live.AttemptLoginAsync()).Status);
        Assert.Equal(HttpStatusCode.OK, (await live.GetAsync($"{Api}/auth/me")).Status);

        List<Dictionary<string, object?>> rows;
        await using (var database = await StopForInspectionAsync(server))
        {
            rows = SeedSql.Rows(database.Connection, "SELECT id, revoked_at FROM user_sessions");
        }

        var byId = rows.ToDictionary(row => (string)row["id"]!);
        Assert.True(expiredIds.All(byId.ContainsKey), "the expired sessions were cleaned up before the login ran");
        // The admin's own bootstrap sign-in is a session too, live and unexpired like the one this test's login creates;
        // both are excluded from the deliberately-expired set here.
        var otherIds = expiredIds.Append(bootstrapSessionId!).ToHashSet();
        var liveRows = byId.Where(entry => !otherIds.Contains(entry.Key)).Select(entry => entry.Value).ToList();
        Assert.Single(liveRows);
        Assert.Null(liveRows[0]["revoked_at"]);
        Assert.True(expiredIds.All(id => byId[id]["revoked_at"] is null));
    }

    [Fact]
    public async Task Load_valid_session_throttles_last_seen_persistence()
    {
        await using var server = await WeirServer.StartNewAsync(IdleMinutes720);
        await NewSession(server).EnsureAdminAccountAsync();
        DateTime recentSeen;
        DateTime oldSeen;
        string recentId;
        string recentToken;
        string oldId;
        string oldToken;
        await using (var database = await server.StopForDatabaseAsync())
        {
            var now = UtcNow();
            recentSeen = now;
            oldSeen = now - TimeSpan.FromMinutes(10);
            var userId = UserId(database.Connection, WeirClient.AdminUsername);
            (recentId, recentToken) = InsertSession(database.Connection, userId, oldSeen, now + TimeSpan.FromDays(1), recentSeen);
            (oldId, oldToken) = InsertSession(database.Connection, userId, oldSeen, now + TimeSpan.FromDays(1), oldSeen);
        }

        // Well inside the 60 s touch gap for the recent session; well past it for the old one.
        Assert.Equal(HttpStatusCode.OK, (await SessionWithCookie(server, recentToken).GetAsync($"{Api}/auth/me")).Status);
        Assert.Equal(HttpStatusCode.OK, (await SessionWithCookie(server, oldToken).GetAsync($"{Api}/auth/me")).Status);

        Dictionary<string, string> seen;
        await using (var database = await StopForInspectionAsync(server))
        {
            seen = SeedSql.Rows(database.Connection, "SELECT id, last_seen_at FROM user_sessions")
                .ToDictionary(row => (string)row["id"]!, row => (string)row["last_seen_at"]!);
        }

        Assert.Equal(recentSeen, ParseUtcText(seen[recentId]));
        Assert.True(ParseUtcText(seen[oldId]) > oldSeen + TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Expired_session_is_rejected_and_revoked()
    {
        await using var server = await WeirServer.StartNewAsync(IdleMinutes720);
        await NewSession(server).EnsureAdminAccountAsync();
        DateTime expires;
        string sessionId;
        string token;
        await using (var database = await server.StopForDatabaseAsync())
        {
            var now = UtcNow();
            expires = now + ExpiryMargin;
            (sessionId, token) = InsertSession(database.Connection, UserId(database.Connection, WeirClient.AdminUsername), now, expires, now);
        }

        await WaitPastAsync(expires);

        var expired = SessionWithCookie(server, token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await expired.GetAsync($"{Api}/auth/me")).Status);
        // A 401 rolls the request's writes back, so the revocation is persisted by a request that loads the session and
        // still succeeds: the CSRF fetch every page makes first (it falls back to an anonymous token).
        Assert.Equal(HttpStatusCode.OK, (await expired.GetAsync($"{Api}/auth/csrf")).Status);

        List<Dictionary<string, object?>> rows;
        await using (var database = await StopForInspectionAsync(server))
        {
            rows = SeedSql.Rows(database.Connection, "SELECT revoked_at FROM user_sessions WHERE id = $id", ("$id", sessionId));
        }

        Assert.True(rows.Count > 0, "the session was cleaned up before the request ran");
        Assert.NotNull(rows[0]["revoked_at"]);
    }

    /// <summary>The cleanup runs at server start (and hourly); a restart is the black-box trigger.</summary>
    [Fact]
    public async Task Session_cleanup_deletes_revoked_and_expired_sessions()
    {
        await using var server = await WeirServer.StartNewAsync(IdleMinutes720);
        // The admin's own bootstrap sign-in is itself a still-live, unexpired session, alongside the ones seeded directly
        // below; cleanup must leave both it and the active one, and delete only the revoked and expired rows.
        var bootstrapSessionId = await NewSession(server).EnsureAdminAccountAsync();
        string activeId;
        await using (var database = await server.StopForDatabaseAsync())
        {
            var now = UtcNow();
            var userId = UserId(database.Connection, WeirClient.AdminUsername);
            var later = now + TimeSpan.FromDays(1);
            InsertSession(database.Connection, userId, now, later, now, revokedAt: now);
            InsertSession(database.Connection, userId, now, now - TimeSpan.FromSeconds(1), now);
            activeId = InsertSession(database.Connection, userId, now, later, now).Id;
        }

        List<string> ids;
        await using (var database = await StopForInspectionAsync(server))
        {
            ids = SeedSql.Rows(database.Connection, "SELECT id FROM user_sessions").Select(row => (string)row["id"]!).ToList();
        }

        Assert.Equal(new HashSet<string> { activeId, bootstrapSessionId! }, ids.ToHashSet());
    }

    private WeirClient SessionWithCookie(WeirServer server, string token)
    {
        var session = NewSession(server);
        session.SetCookie(AuthSupport.SessionCookie, token);
        return session;
    }
}
