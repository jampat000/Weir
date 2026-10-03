using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>A server with the admin <c>alice</c> and the viewer <c>bob</c> seeded after its first start.</summary>
public class UsersFixture : SeededServerFixture
{
    protected override void Seed(SqliteConnection connection) => SystemPartBHelpers.SeedUsers(connection);
}

/// <summary>Users seeded, and no route to the internet (the update status route asks GitHub for the latest release).</summary>
public sealed class NoInternetUsersFixture : UsersFixture
{
    protected override IReadOnlyDictionary<string, string> Environment => SystemPartBHelpers.NoInternet;
}
