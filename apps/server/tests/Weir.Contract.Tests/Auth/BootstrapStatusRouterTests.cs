using System.Net;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Auth.AuthSupport;

namespace Weir.Contract.Tests.Auth;

/// <summary>The bootstrap status probe when the database fails. Black-box, the one failure a running server can be put into is a database whose <c>users</c> table is missing.</summary>
[ContractArea("auth")]
public sealed class BootstrapStatusRouterTests
{
    /// <summary>Guest-first probe: never HTTP 500; a missing schema is a 503 that says so.</summary>
    [Fact]
    public async Task Bootstrap_status_missing_table_returns_503()
    {
        await using var server = await WeirServer.StartNewAsync();
        await using (var database = await server.StopForDatabaseAsync())
        {
            SeedSql.Execute(database.Connection, "PRAGMA foreign_keys=OFF");
            SeedSql.Execute(database.Connection, "DROP TABLE users");
        }

        using var session = new AuthSession(server.BaseUrl);
        var response = await session.GetAsync($"{AuthSession.Api}/auth/bootstrap/status");

        AssertStatus(HttpStatusCode.ServiceUnavailable, response);
        var detail = response.Fields["detail"]?.GetValue<string>();
        Assert.True(detail is { Length: > 10 });
        Assert.Contains("schema", detail.ToLowerInvariant(), StringComparison.Ordinal);
    }
}
