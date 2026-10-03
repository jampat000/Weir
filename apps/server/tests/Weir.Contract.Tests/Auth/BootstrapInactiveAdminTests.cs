using System.Net;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Auth.AuthSupport;

namespace Weir.Contract.Tests.Auth;

/// <summary>A deactivated sole admin must not brick the install (#456): sign-in rejects it, so bootstrap reopens.</summary>
[ContractArea("auth")]
public sealed class BootstrapInactiveAdminTests
{
    private const string Api = AuthSession.Api;

    [Fact]
    public async Task Active_admin_still_closes_bootstrap()
    {
        await using var server = await SeededServerAsync(active: true);
        using var session = new AuthSession(server.BaseUrl);

        Assert.False((bool)(await session.GetAsync($"{Api}/auth/bootstrap/status")).Fields["bootstrap_allowed"]!);
        Assert.Equal(HttpStatusCode.OK, (await session.LoginAsync()).Status);
    }

    [Fact]
    public async Task Inactive_sole_admin_reopens_bootstrap()
    {
        await using var server = await SeededServerAsync(active: false);
        using var session = new AuthSession(server.BaseUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, (await session.LoginAsync()).Status);
        Assert.True((bool)(await session.GetAsync($"{Api}/auth/bootstrap/status")).Fields["bootstrap_allowed"]!);
    }

    /// <summary>The install must come back with one usable admin, never two rows.</summary>
    [Fact]
    public async Task Recovering_from_an_inactive_admin_leaves_exactly_one()
    {
        await using var server = await SeededServerAsync(active: false);
        using var session = new AuthSession(server.BaseUrl);

        var created = await session.BootstrapAsync("alice-again", "recovered-password-strong");

        AssertStatus(HttpStatusCode.OK, created);
        Assert.False((bool)(await session.GetAsync($"{Api}/auth/bootstrap/status")).Fields["bootstrap_allowed"]!);
        Assert.Equal(HttpStatusCode.OK, (await session.LoginAsync("alice-again", "recovered-password-strong")).Status);

        List<Dictionary<string, object?>> admins;
        await using (var database = await StopForInspectionAsync(server))
        {
            admins = SeedSql.Rows(database.Connection, "SELECT username, is_active FROM users WHERE role = 'admin'");
        }

        var admin = Assert.Single(admins);
        Assert.Equal("alice-again", admin["username"]);
        Assert.Equal(1L, Convert.ToInt64(admin["is_active"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(2, admin.Count);
    }

    private static async Task<WeirServer> SeededServerAsync(bool active)
    {
        var server = await WeirServer.StartNewAsync();
        try
        {
            await using var database = await server.StopForDatabaseAsync();
            SeedSql.InsertUser(database.Connection, WeirClient.AdminUsername, AdminPasswordHash, "admin", active);
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }

        return server;
    }
}
