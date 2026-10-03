using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>Users, environment and sign-in shared by the System contract classes of part B.</summary>
internal static class SystemPartBHelpers
{
    public const string Api = WeirClient.Api;

    public const string ExistingAdminUsername = "existing-admin";
    public const string ExistingAdminPassword = "existing-admin-password";
    public const string ExistingAdminPasswordHash =
        "$argon2id$v=19$m=65536,t=3,p=1$o1xjUc27A7Yoqn6qebapuQ$G8paeioeYD8vF+4TcnooWCWTBrbPk5VawNi2G8+0awI";

    // Every outbound HTTP(S) client the server builds from the environment goes through this proxy, which refuses
    // connections: an update check can never reach the internet from a contract run.
    public static readonly IReadOnlyDictionary<string, string> NoInternet = new Dictionary<string, string>
    {
        ["HTTPS_PROXY"] = "http://127.0.0.1:9",
        ["HTTP_PROXY"] = "http://127.0.0.1:9",
        ["ALL_PROXY"] = "http://127.0.0.1:9",
        ["NO_PROXY"] = string.Empty,
    };

    /// <summary>
    /// Seeds the admin <c>alice</c> and the viewer <c>bob</c> into a stopped database. The admin is seeded too
    /// because bootstrap is refused once any user exists.
    /// </summary>
    public static void SeedUsers(SqliteConnection connection)
    {
        SeededAccounts.EnsureAdmin(connection);
        SeededAccounts.EnsureViewer(connection);
    }
}
