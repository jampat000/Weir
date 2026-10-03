using System.Globalization;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Libraries;

/// <summary>The viewer account bob, seeded while a server is stopped because no API creates a user without an admin.</summary>
internal static class LibrariesPartBViewer
{
    public const string Username = "bob";
    public const string Password = "viewer-password-here";

    // Argon2id (PHC format, as users.password_hash holds it) of Password: the suite has no hasher of its own.
    private const string PasswordHash =
        "$argon2id$v=19$m=65536,t=3,p=1$1FAZDPY1Q+jK6pqz4PUmDQ$xdxYW1ObpjuQPUugXzhpJTy8i3qLJQpca61EfWZ6gJI";

    /// <summary>Adds bob when the server has no such user; the server restarts on a new port, so create clients afterwards.</summary>
    public static async Task EnsureAsync(WeirServer server)
    {
        await using var database = await server.StopForDatabaseAsync();
        var existing = SeedSql.Scalar(database.Connection, "SELECT COUNT(*) FROM users WHERE username = $username", ("$username", Username));
        if (Convert.ToInt64(existing, CultureInfo.InvariantCulture) == 0)
        {
            SeedSql.InsertUser(database.Connection, Username, PasswordHash);
        }
    }

    public static async Task<WeirClient> SignInAsync(WeirServer server)
    {
        var client = server.CreateClient();
        try
        {
            await client.LoginAsync(Username, Password);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        return client;
    }
}
