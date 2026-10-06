using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Weir.Contract.Tests.Harness;

/// <summary>Plain SQL against the shared schema, for a <see cref="StoppedDatabase"/>.</summary>
public static class SeedSql
{
    private const int TicksPerMicrosecond = 10;

    /// <summary>A timestamp as the schema stores it: <c>YYYY-MM-DD HH:MM:SS</c>, plus <c>.ffffff</c> unless it is a whole second.</summary>
    public static string UtcText(DateTime moment)
    {
        var utc = moment.ToUniversalTime();
        var text = utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var microseconds = utc.Ticks % TimeSpan.TicksPerSecond / TicksPerMicrosecond;
        return microseconds == 0 ? text : $"{text}.{microseconds:D6}";
    }

    public static int Execute(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        return command.ExecuteNonQuery();
    }

    public static long InsertAndGetId(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        Execute(connection, sql, parameters);
        return Convert.ToInt64(Scalar(connection, "SELECT last_insert_rowid()"), CultureInfo.InvariantCulture);
    }

    public static object? Scalar(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        var value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    public static List<Dictionary<string, object?>> Rows(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        using var reader = command.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            rows.Add(Enumerable.Range(0, reader.FieldCount)
                .ToDictionary(column => reader.GetName(column), column => reader.IsDBNull(column) ? null : reader.GetValue(column)));
        }

        return rows;
    }

    public static long InsertUser(
        SqliteConnection connection, string username, string passwordHash, string role = "viewer", bool isActive = true)
    {
        var now = UtcText(DateTime.UtcNow);
        return InsertAndGetId(
            connection,
            "INSERT INTO users (username, password_hash, role, is_active, created_at, updated_at) "
                + "VALUES ($username, $hash, $role, $active, $now, $now)",
            ("$username", username), ("$hash", passwordHash), ("$role", role), ("$active", isActive ? 1 : 0), ("$now", now));
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }
}
