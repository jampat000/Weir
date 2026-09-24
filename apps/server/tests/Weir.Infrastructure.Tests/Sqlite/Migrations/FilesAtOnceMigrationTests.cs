using System.Globalization;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// Migration <c>0011_files_at_once.sql</c>: an upgrade changes nothing about how many files run today,
/// and an install that never tuned anything gets the simple model - a library follows "Files at once", and the
/// resolution budget is off.
/// <para>Each test builds a database at head, puts it back the way it was before this migration (no budget switch, the saved limits
/// under test), and runs the migration's own SQL.</para>
/// </summary>
/// <seealso href="https://github.com/jampat000/Weir/issues/633"/>
public sealed class FilesAtOnceMigrationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public FilesAtOnceMigrationTests()
    {
        _database = new SqliteDatabase(_temp.Join("weir.sqlite3"));
        new SchemaMigrator(_database).EnsureAtHead();
        Execute("ALTER TABLE operator_settings DROP COLUMN runner_budget_enabled");
        Execute("DELETE FROM libraries");
        Execute("INSERT INTO libraries (id, name, media_type, watched_folder, display_order, max_concurrent_files) VALUES (1, 'Movies', 'movie', '/in', 1, 1)");
        Execute("INSERT INTO libraries (id, name, media_type, watched_folder, display_order, max_concurrent_files) VALUES (2, 'TV', 'tv', '/tv', 2, 3)");
    }

    public void Dispose()
    {
        _database.ClearPool();
        _temp.Dispose();
    }

    private void Migrate() => Execute(SchemaMigrator.ReadMigrationSql(SchemaMigrator.Migrations.Single(migration => migration.Number == 11)));

    [Fact]
    public void An_install_that_never_tuned_anything_gets_the_simple_model()
    {
        Execute(
            "UPDATE operator_settings SET max_concurrent_files = 1, runner_capacity = 4, runner_cost_sd = 1, runner_cost_720p = 1, " +
            "runner_cost_1080p = 2, runner_cost_4k = 4, runner_cost_undetermined = 0");

        Migrate();

        Assert.Equal(0, Scalar("SELECT runner_budget_enabled FROM operator_settings"));
        // Movies was at the old default of 1, the same thing as "Files at once" = 1 today; TV's 3 was somebody's choice.
        Assert.Equal(0, Scalar("SELECT max_concurrent_files FROM libraries WHERE id = 1"));
        Assert.Equal(3, Scalar("SELECT max_concurrent_files FROM libraries WHERE id = 2"));
    }

    [Fact]
    public void An_install_already_running_several_at_once_keeps_every_limit_it_had()
    {
        Execute("UPDATE operator_settings SET max_concurrent_files = 4");

        Migrate();

        Assert.Equal(1, Scalar("SELECT runner_budget_enabled FROM operator_settings"));
        Assert.Equal(1, Scalar("SELECT max_concurrent_files FROM libraries WHERE id = 1"));
        Assert.Equal(3, Scalar("SELECT max_concurrent_files FROM libraries WHERE id = 2"));
    }

    [Fact]
    public void Costs_that_keep_a_resolution_from_ever_starting_keep_doing_so()
    {
        Execute("UPDATE operator_settings SET max_concurrent_files = 1, runner_capacity = 2, runner_cost_4k = 4");

        Migrate();

        Assert.Equal(1, Scalar("SELECT runner_budget_enabled FROM operator_settings"));
    }

    [Fact]
    public void A_new_install_starts_with_the_simple_model()
    {
        using var fresh = new TempDirectory();
        var database = new SqliteDatabase(fresh.Join("weir.sqlite3"));
        try
        {
            new SchemaMigrator(database).EnsureAtHead();
            using var connection = database.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT (SELECT runner_budget_enabled FROM operator_settings WHERE id = 1) || '|' || " +
                "(SELECT coalesce(group_concat(max_concurrent_files, ','), '') FROM libraries)";
            var text = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture)!;
            Assert.StartsWith("0|", text, StringComparison.Ordinal);
            Assert.DoesNotContain("1", text[2..], StringComparison.Ordinal);
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

    private long Scalar(string sql)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
