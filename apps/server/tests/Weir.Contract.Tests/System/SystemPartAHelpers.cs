using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>Users and sign-in shared by the first half of the system area's test classes.</summary>
internal static class SystemPartAHelpers
{
    public const string ViewerUsername = "bob";
    public const string ViewerPassword = "viewer-password-here";

    // Argon2id hashes of WeirClient.AdminPassword and ViewerPassword, made with the parameters the server uses
    // (time 3, memory 65536 KiB, parallelism 1, 32-byte hash, 16-byte salt), because the harness has no Argon2.
    private const string AdminPasswordHash =
        "$argon2id$v=19$m=65536,t=3,p=1$x5a2YZD4umGh419gQoufBA$MZKIfdyVMb7sfBF/VcfK6hyqaBq3ULBltkJepiVP1AA";

    private const string ViewerPasswordHash =
        "$argon2id$v=19$m=65536,t=3,p=1$xTJejaOHier0U8uyRdcjtA$UIw2mm0HNYdLY75kzLgRRQmin+MkSW9zcxM8PzxthE4";

    /// <summary>
    /// Seeds the admin <c>alice</c> and the viewer <c>bob</c> while the server is stopped (it restarts on a new port).
    /// The admin is seeded too because bootstrap is refused once any user exists.
    /// </summary>
    public static async Task SeedUsersAsync(WeirServer server)
    {
        await using var database = await server.StopForDatabaseAsync();
        SeedSql.InsertUser(database.Connection, WeirClient.AdminUsername, AdminPasswordHash, "admin");
        SeedSql.InsertUser(database.Connection, ViewerUsername, ViewerPasswordHash, "viewer");
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
}
