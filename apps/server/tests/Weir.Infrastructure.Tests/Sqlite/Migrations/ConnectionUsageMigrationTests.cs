using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// Migration <c>0036_connection_usage.sql</c>: every existing media manager and download client connection keeps its data and
/// starts with no recorded answer time and no last-used time.
/// <para>Each test builds a database at head, takes the four columns back out, adds a connection of each kind as an earlier
/// version would have stored it, and runs the migration's own SQL.</para>
/// </summary>
public sealed class ConnectionUsageMigrationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public ConnectionUsageMigrationTests()
    {
        _database = new SqliteDatabase(_temp.Join("weir.sqlite3"));
        new SchemaMigrator(_database).EnsureAtHead();
        foreach (var table in new[] { "media_manager_connections", "download_client_connections" })
        {
            Execute($"ALTER TABLE {table} DROP COLUMN last_answer_ms");
            Execute($"ALTER TABLE {table} DROP COLUMN last_used_at");
        }

        Execute("INSERT INTO media_manager_connections (id, kind, name, base_url) VALUES (1, 'radarr', 'Radarr on nas', 'http://nas:7878')");
        Execute("INSERT INTO download_client_connections (id, kind, name, base_url) VALUES (1, 'qbittorrent', 'qBittorrent on nas', 'http://nas:8080')");
    }

    public void Dispose()
    {
        _database.ClearPool();
        _temp.Dispose();
    }

    private void Migrate() => Execute(SchemaMigrator.ReadMigrationSql(SchemaMigrator.Migrations.Single(migration => migration.Number == 36)));

    [Theory]
    [InlineData("media_manager_connections", "Radarr on nas")]
    [InlineData("download_client_connections", "qBittorrent on nas")]
    public void An_existing_connection_keeps_its_name_and_has_no_usage_recorded(string table, string name)
    {
        Migrate();

        Assert.Equal(name, Scalar($"SELECT name FROM {table} WHERE id = 1"));
        Assert.Equal(DBNull.Value, Scalar($"SELECT last_answer_ms FROM {table} WHERE id = 1"));
        Assert.Equal(DBNull.Value, Scalar($"SELECT last_used_at FROM {table} WHERE id = 1"));
    }

    private void Execute(string sql)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private object? Scalar(string sql)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
