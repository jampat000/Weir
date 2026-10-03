using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Auth;

/// <summary>Stateless helpers the auth tests share: seeded accounts, session rows and the clock.</summary>
public static class AuthSupport
{
    public const string SessionCookie = "weir_session";
    public const string ViewerUsername = "bob";
    public const string ViewerPassword = "viewer-password-here";

    // Argon2id PHC strings (m=65536, t=3, p=1, 32-byte hash, 16-byte salt), the format users.password_hash holds.
    public const string ViewerPasswordHash = "$argon2id$v=19$m=65536,t=3,p=1$EcJ710LJQO79Yp1EJYGJTw$voE4PjB0y5u5+mnoY4cQANEnKkWPG6VUFYBwLxTDy/g";
    public const string AdminPasswordHash = "$argon2id$v=19$m=65536,t=3,p=1$CJlvLLyHD9vjw57xenosZQ$TVsrhsG8/sn4eKb33TmwEI47n9hHHfm62NI4etjvJ7k";
    public const string UnusedPasswordHash = "$argon2id$v=19$m=65536,t=3,p=1$JDfarhm32MId/nK3lvQuWQ$LBjRvv2RMp0vfRF+ZnxBWLBRewotDO5Ior/7Q1c7dDI";

    private const int TicksPerMicrosecond = 10;
    private const int TokenBytes = 32;

    /// <summary>Seeds the viewer <c>bob</c> while the server is stopped; the server comes back on a new port.</summary>
    public static async Task EnsureViewerAsync(WeirServer server)
    {
        await using var database = await server.StopForDatabaseAsync();
        var existing = SeedSql.Scalar(database.Connection, "SELECT COUNT(*) FROM users WHERE username = $name", ("$name", ViewerUsername));
        if (Convert.ToInt64(existing, CultureInfo.InvariantCulture) == 0)
        {
            SeedSql.InsertUser(database.Connection, ViewerUsername, ViewerPasswordHash, "viewer");
        }
    }

    /// <summary>Fails with what the server actually answered unless the status is the expected one.</summary>
    public static void AssertStatus(HttpStatusCode expected, WeirResponse response) =>
        Assert.True(response.Status == expected, response.ToString());

    public static IReadOnlyDictionary<string, string> Headers(params (string Name, string Value)[] headers) =>
        headers.ToDictionary(header => header.Name, header => header.Value);

    /// <summary>Stops the server and opens its database for reading; the server stays stopped until the test ends.</summary>
    public static async Task<StoppedDatabase> StopForInspectionAsync(WeirServer server)
    {
        await server.StopAsync();
        return await StoppedDatabase.OpenAsync(server.DatabasePath, () => Task.CompletedTask);
    }

    public static string SetCookieHeader(WeirResponse response) => response.Header("Set-Cookie") ?? string.Empty;

    // --- sessions in SQLite ---------------------------------------------------------------------

    public static long UserId(SqliteConnection connection, string username) =>
        Convert.ToInt64(
            SeedSql.Scalar(connection, "SELECT id FROM users WHERE username = $name", ("$name", username))
                ?? throw new InvalidOperationException($"no user '{username}'"),
            CultureInfo.InvariantCulture);

    /// <summary>Inserts a <c>user_sessions</c> row. Returns the session id (hex) and the raw cookie token.</summary>
    public static (string Id, string Token) InsertSession(
        SqliteConnection connection,
        long user,
        DateTime createdAt,
        DateTime absoluteExpiresAt,
        DateTime lastSeenAt,
        DateTime? revokedAt = null)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(TokenBytes));
        var id = Guid.NewGuid().ToString("N");
        SeedSql.Execute(
            connection,
            "INSERT INTO user_sessions (id, user_id, token_hash, created_at, absolute_expires_at, is_trusted_device, "
                + "last_seen_at, revoked_at, client_label) "
                + "VALUES ($id, $user, $hash, $created, $expires, 0, $seen, $revoked, 'Browser session')",
            ("$id", id),
            ("$user", user),
            ("$hash", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)))),
            ("$created", SeedSql.UtcText(createdAt)),
            ("$expires", SeedSql.UtcText(absoluteExpiresAt)),
            ("$seen", SeedSql.UtcText(lastSeenAt)),
            ("$revoked", revokedAt is null ? null : SeedSql.UtcText(revokedAt.Value)));
        return (id, token);
    }

    // --- the clock ------------------------------------------------------------------------------

    /// <summary>Now, to the microsecond the schema stores, so a seeded time reads back equal.</summary>
    public static DateTime UtcNow()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - (now.Ticks % TicksPerMicrosecond), DateTimeKind.Utc);
    }

    public static DateTime ParseUtcText(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    /// <summary>
    /// Waits until the wall clock is past <paramref name="moment"/>, a deadline the test chose. It exists for the case where
    /// the running server's own clock must carry a still-live session past its expiry, so the request-time check is what
    /// gets exercised: seeding the row pre-expired needs a restart, and the server deletes expired rows when it starts.
    /// </summary>
    public static Task WaitPastAsync(DateTime moment) =>
        Poll.UntilAsync(() => Task.FromResult(DateTime.UtcNow > moment.AddMilliseconds(500)), "the wall clock to pass the session's expiry", TimeSpan.FromMinutes(2));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
