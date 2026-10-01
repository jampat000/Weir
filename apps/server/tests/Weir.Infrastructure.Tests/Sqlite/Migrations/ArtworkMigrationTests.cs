using System.Globalization;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// Migration <c>0035_artwork.sql</c>: the poster tables, and the Artwork setting, which an install that already has settings
/// starts with switched on.
/// <para>Each test builds a database at the revision before the migration and runs the migration's own SQL.</para>
/// </summary>
public sealed class ArtworkMigrationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public ArtworkMigrationTests()
    {
        _database = new SqliteDatabase(_temp.Join("weir.sqlite3"));
        new SchemaMigrator(_database).EnsureAtHead();
        Execute("DROP TABLE artwork_files");
        Execute("DROP TABLE artwork_lookups");
        Execute("DROP TABLE artwork_posters");
        Execute("ALTER TABLE suite_settings DROP COLUMN artwork_enabled");
    }

    public void Dispose()
    {
        _database.ClearPool();
        _temp.Dispose();
    }

    private void Migrate() => Execute(SchemaMigrator.ReadMigrationSql(SchemaMigrator.Migrations.Single(migration => migration.Number == 35)));

    [Fact]
    public void Existing_settings_start_with_artwork_on()
    {
        Migrate();

        Assert.Equal(1, Scalar("SELECT artwork_enabled FROM suite_settings WHERE id = 1"));
    }

    [Fact]
    public void The_poster_tables_exist_and_are_empty()
    {
        Migrate();

        Assert.Equal(0, Scalar("SELECT count(*) FROM artwork_lookups"));
        Assert.Equal(0, Scalar("SELECT count(*) FROM artwork_posters"));
        Assert.Equal(0, Scalar("SELECT count(*) FROM artwork_files"));
    }

    [Fact]
    public void A_lookup_starts_pending_so_it_is_asked_about_once()
    {
        Migrate();
        Execute("INSERT INTO artwork_lookups (lookup_key, media_scope, title) VALUES ('title:movie:x:0', 'movie', 'x')");

        Assert.Equal(1, Scalar("SELECT count(*) FROM artwork_lookups WHERE outcome = 'pending' AND attempts = 0 AND retry_at IS NULL"));
    }

    [Fact]
    public void A_file_row_starts_unmarked_and_the_mark_is_indexed()
    {
        Migrate();
        Execute("INSERT INTO artwork_files (library_id, relative_path) VALUES (1, 'a.mkv')");

        Assert.Equal(1, Scalar("SELECT count(*) FROM artwork_files WHERE orphaned_at IS NULL"));
        Assert.Equal(1, Scalar("SELECT count(*) FROM sqlite_master WHERE type = 'index' AND name = 'ix_artwork_files_orphaned_at'"));
    }

    [Fact]
    public void Forgetting_a_lookup_forgets_the_files_that_pointed_at_it()
    {
        Migrate();
        Execute("INSERT INTO artwork_lookups (lookup_key, media_scope, title) VALUES ('k', 'movie', 'x')");
        Execute("INSERT INTO artwork_files (library_id, relative_path, lookup_key) VALUES (1, 'a.mkv', 'k')");
        Execute("PRAGMA foreign_keys = ON; DELETE FROM artwork_lookups WHERE lookup_key = 'k'");

        Assert.Equal(0, Scalar("SELECT count(*) FROM artwork_files"));
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
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
