using System.Globalization;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// Migration <c>0027_library_intake_follows_performance.sql</c>: a library still holding the values it was seeded with
/// starts following Settings › Performance, and a value a person chose is kept.
/// <para>Each test builds a database at head, puts the two columns back the way they were before this migration (required,
/// with the seeded defaults), and runs the migration's own SQL.</para>
/// </summary>
/// <seealso href="https://github.com/jampat000/Weir/issues/815"/>
public sealed class LibraryIntakeFollowsPerformanceMigrationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public LibraryIntakeFollowsPerformanceMigrationTests()
    {
        _database = new SqliteDatabase(_temp.Join("weir.sqlite3"));
        new SchemaMigrator(_database).EnsureAtHead();
        Execute("DELETE FROM libraries");
        RestoreRequiredColumn("min_file_size_mb", "0");
        RestoreRequiredColumn("min_file_age_seconds", "60");
    }

    public void Dispose()
    {
        _database.ClearPool();
        _temp.Dispose();
    }

    private void RestoreRequiredColumn(string column, string defaultValue)
    {
        Execute($"ALTER TABLE libraries ADD COLUMN {column}_required INTEGER DEFAULT '{defaultValue}' NOT NULL");
        Execute($"ALTER TABLE libraries DROP COLUMN {column}");
        Execute($"ALTER TABLE libraries RENAME COLUMN {column}_required TO {column}");
    }

    private void Migrate() => Execute(SchemaMigrator.ReadMigrationSql(SchemaMigrator.Migrations.Single(migration => migration.Number == 27)));

    private void Library(int id, long minFileSizeMb, long minFileAgeSeconds) =>
        Execute(
            "INSERT INTO libraries (id, name, media_type, watched_folder, display_order, min_file_size_mb, min_file_age_seconds) " +
            $"VALUES ({id}, 'Library {id}', 'movie', '/in{id}', {id}, {minFileSizeMb}, {minFileAgeSeconds})");

    private bool Follows(int id, string column) => Scalar($"SELECT {column} IS NULL FROM libraries WHERE id = {id}") == 1;

    [Fact]
    public void A_library_still_holding_the_seeded_values_follows_Performance()
    {
        Library(1, minFileSizeMb: 50, minFileAgeSeconds: 60);

        Migrate();

        Assert.True(Follows(1, "min_file_size_mb"));
        Assert.True(Follows(1, "min_file_age_seconds"));
    }

    [Fact]
    public void A_minimum_size_of_zero_follows_Performance_because_a_pass_already_skipped_files_under_it()
    {
        Library(1, minFileSizeMb: 0, minFileAgeSeconds: 60);

        Migrate();

        Assert.True(Follows(1, "min_file_size_mb"));
    }

    [Fact]
    public void Values_a_person_chose_are_kept()
    {
        Library(1, minFileSizeMb: 200, minFileAgeSeconds: 300);

        Migrate();

        Assert.Equal(200, Scalar("SELECT min_file_size_mb FROM libraries WHERE id = 1"));
        Assert.Equal(300, Scalar("SELECT min_file_age_seconds FROM libraries WHERE id = 1"));
    }

    [Fact]
    public void A_wait_of_zero_is_a_choice_and_is_kept()
    {
        Library(1, minFileSizeMb: 50, minFileAgeSeconds: 0);

        Migrate();

        Assert.True(Follows(1, "min_file_size_mb"));
        Assert.Equal(0, Scalar("SELECT min_file_age_seconds FROM libraries WHERE id = 1"));
    }

    [Fact]
    public void A_library_is_otherwise_untouched()
    {
        Library(1, minFileSizeMb: 50, minFileAgeSeconds: 60);

        Migrate();

        Assert.Equal("Library 1", ScalarText("SELECT name FROM libraries WHERE id = 1"));
        Assert.Equal("/in1", ScalarText("SELECT watched_folder FROM libraries WHERE id = 1"));
    }

    [Fact]
    public void A_new_install_starts_with_every_library_following_Performance()
    {
        using var fresh = new TempDirectory();
        var database = new SqliteDatabase(fresh.Join("weir.sqlite3"));
        try
        {
            new SchemaMigrator(database).EnsureAtHead();
            using var connection = database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM libraries WHERE min_file_size_mb IS NOT NULL OR min_file_age_seconds IS NOT NULL";
            Assert.Equal(0, Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture));
        }
        finally
        {
            database.ClearPool();
        }
    }

    private void Execute(string sql)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long Scalar(string sql) => Convert.ToInt64(ScalarText(sql), CultureInfo.InvariantCulture);

    private string ScalarText(string sql)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
