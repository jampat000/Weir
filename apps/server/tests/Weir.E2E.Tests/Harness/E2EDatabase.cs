using System.Globalization;
using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;

namespace Weir.E2E.Tests.Harness;

/// <summary>
/// Plain-SQL resets and seeds against the running server's SQLite file, the way an operator's second process
/// would write to it. The server does not cache the rows touched here.
/// </summary>
public static class E2EDatabase
{
    private const int BusyTimeoutSeconds = 30;

    /// <summary>
    /// Resets all per-test state so each test starts at the setup page. Users, sessions and suite settings are
    /// cleared. The migration-seeded Processing libraries have their folders cleared rather than being deleted,
    /// because they are the only path store and a scope with no library at all has nowhere to resolve to.
    /// </summary>
    public static void ResetPerTestState(string databasePath)
    {
        using var connection = Open(databasePath);
        using var transaction = connection.BeginTransaction();
        SeedSql.Execute(connection, "UPDATE libraries SET watched_folder = '', work_folder = '', output_folder = ''");
        SeedSql.Execute(connection, "DELETE FROM user_sessions");
        SeedSql.Execute(connection, "DELETE FROM users");
        SeedSql.Execute(connection, "DELETE FROM suite_settings");
        transaction.Commit();
    }

    /// <summary>One <c>activity_events</c> row, stamped now in the stored shape (<c>YYYY-MM-DD HH:MM:SS.ffffff</c>, UTC).</summary>
    public static void InsertActivityEvent(string databasePath, string eventType, string module, string title, string? detail = null)
    {
        var createdAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
        using var connection = Open(databasePath);
        using var transaction = connection.BeginTransaction();
        SeedSql.Execute(
            connection,
            "INSERT INTO activity_events (created_at, event_type, module, title, detail) VALUES ($created, $type, $module, $title, $detail)",
            ("$created", createdAt),
            ("$type", eventType),
            ("$module", module),
            ("$title", title),
            ("$detail", string.IsNullOrEmpty(detail) ? null : detail));
        transaction.Commit();
    }

    private static SqliteConnection Open(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            DefaultTimeout = BusyTimeoutSeconds,
        }.ToString());
        connection.Open();
        SeedSql.Execute(connection, $"PRAGMA busy_timeout = {BusyTimeoutSeconds * 1000}");
        return connection;
    }
}
