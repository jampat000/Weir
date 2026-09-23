using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// #651's migration (<c>0014_link_imported_libraries.sql</c>): a library imported from a media manager before imports
/// wrote the link gets it now, and a link that is already there is left alone.
/// </summary>
public sealed class LinkImportedLibrariesMigrationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public LinkImportedLibrariesMigrationTests()
    {
        _database = new SqliteDatabase(_temp.Join("weir.sqlite3"));
        new SchemaMigrator(_database).EnsureAtHead();
    }

    public void Dispose()
    {
        _database.ClearPool();
        _temp.Dispose();
    }

    [Fact]
    public void An_imported_library_is_linked_to_its_manager_and_a_hand_made_one_is_not()
    {
        Execute("INSERT INTO media_manager_connections (id, kind, name) VALUES (7, 'sonarr', 'Sonarr')");
        Execute("INSERT INTO libraries (id, name, media_type, discovered_from_connection_id) VALUES (21, 'Imported TV', 'tv', 7)");
        Execute("INSERT INTO libraries (id, name, media_type) VALUES (22, 'Hand-made videos', 'movie')");
        Execute("DELETE FROM library_manager_links");

        RunMigration();
        RunMigration();

        Assert.Equal(1L, Scalar("SELECT count(*) FROM library_manager_links"));
        Assert.Equal(1L, Scalar("SELECT count(*) FROM library_manager_links WHERE library_id = 21 AND connection_id = 7"));
    }

    private void RunMigration()
    {
        using var stream = typeof(SchemaMigrator).Assembly.GetManifestResourceStream(
            "Weir.Infrastructure.Migrations.0014_link_imported_libraries.sql")!;
        using var reader = new StreamReader(stream);
        Execute(reader.ReadToEnd());
    }

    private void Execute(string sql)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long Scalar(string sql)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }
}
