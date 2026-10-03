using System.Globalization;
using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Jobs;

/// <summary>A read-only user for the role checks: seeded while the server is stopped, since no API makes one.</summary>
internal static class ViewerAccount
{
    public const string Username = "bob";
    public const string Password = "viewer-password-here";

    // Argon2id (t=3, m=65536, p=1) of Password, the PHC format users.password_hash holds.
    private const string PasswordHash =
        "$argon2id$v=19$m=65536,t=3,p=1$W/Yfv8t01vypWmLHAJTXoA$nOvqM9VMx0SvDIVW+GQjoJKrYVOocXC5GKCQZpg7dT8";

    public static void Ensure(SqliteConnection connection)
    {
        var existing = Convert.ToInt64(
            SeedSql.Scalar(connection, "SELECT COUNT(*) FROM users WHERE username = $username", ("$username", Username)),
            CultureInfo.InvariantCulture);
        if (existing == 0)
        {
            SeedSql.InsertUser(connection, Username, PasswordHash, "viewer");
        }
    }
}
