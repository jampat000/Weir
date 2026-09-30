using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// Migration <c>0028_connection_nickname.sql</c>: every existing media manager and download client connection keeps its
/// data and starts with no nickname.
/// <para>Each test builds a database at head, takes the two columns back out, adds a connection of each kind as an
/// earlier version would have stored it, and runs the migration's own SQL.</para>
/// </summary>
public sealed class ConnectionNicknameMigrationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public ConnectionNicknameMigrationTests()
    {
        _database = new SqliteDatabase(_temp.Join("weir.sqlite3"));
        new SchemaMigrator(_database).EnsureAtHead();
        Execute("ALTER TABLE media_manager_connections DROP COLUMN nickname");
        Execute("ALTER TABLE download_client_connections DROP COLUMN nickname");
        Execute("INSERT INTO media_manager_connections (id, kind, name, base_url) VALUES (1, 'radarr', 'Radarr on nas', 'http://nas:7878')");
        Execute("INSERT INTO download_client_connections (id, kind, name, base_url) VALUES (1, 'qbittorrent', 'qBittorrent on nas', 'http://nas:8080')");
    }

    public void Dispose()
    {
        _database.ClearPool();
        _temp.Dispose();
    }

    private void Migrate() => Execute(SchemaMigrator.ReadMigrationSql(SchemaMigrator.Migrations.Single(migration => migration.Number == 28)));

    [Fact]
    public void An_existing_media_manager_keeps_its_name_and_has_no_nickname()
    {
        Migrate();

        Assert.Equal("Radarr on nas", Scalar("SELECT name FROM media_manager_connections WHERE id = 1"));
        Assert.Equal(DBNull.Value, Scalar("SELECT nickname FROM media_manager_connections WHERE id = 1"));
    }

    [Fact]
    public void An_existing_download_client_keeps_its_name_and_has_no_nickname()
    {
        Migrate();

        Assert.Equal("qBittorrent on nas", Scalar("SELECT name FROM download_client_connections WHERE id = 1"));
        Assert.Equal(DBNull.Value, Scalar("SELECT nickname FROM download_client_connections WHERE id = 1"));
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
