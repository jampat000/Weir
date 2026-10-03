using System.Net;
using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Auth.AuthSupport;

namespace Weir.Contract.Tests.Auth;

/// <summary>What sign-in, sign-out and refused bootstraps record in Activity, and how repeats are throttled.</summary>
[ContractArea("auth")]
public sealed class AuthActivityTests(ServerFixture fixture) : AliceTestBase(fixture), IClassFixture<ServerFixture>
{
    [Fact]
    public async Task Login_failed_persisted_throttled_per_username()
    {
        long before;
        await using (var database = await Server.StopForDatabaseAsync())
        {
            SeedSql.Execute(database.Connection, "DELETE FROM activity_events WHERE event_type = 'auth.login_failed' AND detail = 'alice'");
            before = CountEvents(database.Connection, "auth.login_failed", "alice");
        }

        var session = NewSession();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            AssertStatus(HttpStatusCode.Unauthorized, await session.AttemptLoginAsync(password: "wrong-password"));
        }

        long after;
        await using (var database = await Server.StopForDatabaseAsync())
        {
            after = CountEvents(database.Connection, "auth.login_failed", "alice");
        }

        Assert.Equal(1, after - before);
    }

    [Fact]
    public async Task Bootstrap_denied_persisted_throttled()
    {
        long before;
        await using (var database = await Server.StopForDatabaseAsync())
        {
            SeedSql.Execute(database.Connection, "DELETE FROM activity_events WHERE event_type = 'auth.bootstrap_denied'");
            before = CountEvents(database.Connection, "auth.bootstrap_denied");
        }

        var session = NewSession();
        AssertStatus(HttpStatusCode.Forbidden, await session.BootstrapAsync("intruder", "some-long-password-here"));
        AssertStatus(HttpStatusCode.Forbidden, await session.BootstrapAsync("intruder2", "other-long-password-here"));

        long after;
        await using (var database = await Server.StopForDatabaseAsync())
        {
            after = CountEvents(database.Connection, "auth.bootstrap_denied");
        }

        Assert.Equal(1, after - before);
    }

    [Fact]
    public async Task Activity_recent_requires_authentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Alice.GetAsync($"{WeirClient.Api}/activity/recent")).Status);
    }

    [Fact]
    public async Task Activity_recent_includes_login_event()
    {
        AssertStatus(HttpStatusCode.OK, await Alice.AttemptLoginAsync());

        var activity = await Alice.GetAsync($"{WeirClient.Api}/activity/recent");

        AssertStatus(HttpStatusCode.OK, activity);
        Assert.Contains(
            activity.Fields["items"]!.AsArray(),
            item => (string?)item?["event_type"] == "auth.login_succeeded" && (string?)item["detail"] == "alice");
    }

    [Fact]
    public async Task Activity_recent_includes_logout_event()
    {
        await Alice.AttemptLoginAsync();
        AssertStatus(HttpStatusCode.NoContent, await Alice.LogoutWithHeaderAsync());
        await Alice.AttemptLoginAsync();

        var activity = await Alice.GetAsync($"{WeirClient.Api}/activity/recent");

        AssertStatus(HttpStatusCode.OK, activity);
        var types = activity.Fields["items"]!.AsArray().Select(item => (string)item!["event_type"]!).ToList();
        Assert.Contains("auth.login_succeeded", types);
        Assert.Contains("auth.logout", types);
    }

    private static long CountEvents(SqliteConnection connection, string eventType, string? detail = null) =>
        Convert.ToInt64(
            detail is null
                ? SeedSql.Scalar(connection, "SELECT COUNT(*) FROM activity_events WHERE event_type = $type", ("$type", eventType))
                : SeedSql.Scalar(connection, "SELECT COUNT(*) FROM activity_events WHERE event_type = $type AND detail = $detail", ("$type", eventType), ("$detail", detail)),
            System.Globalization.CultureInfo.InvariantCulture);
}
