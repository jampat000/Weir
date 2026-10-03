using System.Globalization;
using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>Users, environment and sign-in shared by the System contract classes of part B.</summary>
internal static class SystemPartBHelpers
{
    public const string Api = WeirClient.Api;
    public const string ViewerUsername = "bob";
    public const string ViewerPassword = "viewer-password-here";

    // Argon2id PHC strings (time 3, memory 65536 KiB, parallelism 1, 32-byte hash, 16-byte salt) of the passwords
    // named beside them: the format users.password_hash holds. The harness has no Argon2 of its own.
    public const string AdminPasswordHash =
        "$argon2id$v=19$m=65536,t=3,p=1$rxMCJBMHkuSBnXPJnPN7ZQ$8TIjJ8Ww+u15LvwnWhZax9ZCvOKXJW0+LC4rZy2b/JQ";

    public const string ViewerPasswordHash =
        "$argon2id$v=19$m=65536,t=3,p=1$sa61FBwTRNQgtRHnjtTclQ$s0omddKYYX1Mm06VB5N2rimN8q2e/2VyRsqCEsQ+qdk";

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
        if (!UserExists(connection, WeirClient.AdminUsername))
        {
            SeedSql.InsertUser(connection, WeirClient.AdminUsername, AdminPasswordHash, "admin");
        }

        if (!UserExists(connection, ViewerUsername))
        {
            SeedSql.InsertUser(connection, ViewerUsername, ViewerPasswordHash, "viewer");
        }
    }

    public static async Task<WeirClient> SignedInViewerAsync(WeirServer server)
    {
        var client = server.CreateClient();
        try
        {
            await client.LoginAsync(ViewerUsername, ViewerPassword);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        return client;
    }

    private static bool UserExists(SqliteConnection connection, string username) =>
        Convert.ToInt64(
            SeedSql.Scalar(connection, "SELECT COUNT(*) FROM users WHERE username = $username", ("$username", username)),
            CultureInfo.InvariantCulture) != 0;
}
