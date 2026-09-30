using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// Migration <c>0029_workflow_free_space.sql</c>: the space Performance kept free becomes each workflow's own, so an
/// upgrade checks every workflow against the value it was checked against before.
/// <para>Each test builds a database at head, takes the new column back out, puts the workflows and Performance's value the
/// way an earlier version stored them, and runs the migration's own SQL.</para>
/// </summary>
public sealed class WorkflowFreeSpaceMigrationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public WorkflowFreeSpaceMigrationTests()
    {
        _database = new SqliteDatabase(_temp.Join("weir.sqlite3"));
        new SchemaMigrator(_database).EnsureAtHead();
        Execute("ALTER TABLE libraries DROP COLUMN minimum_free_disk_space_mb");
        Execute("DELETE FROM libraries");
        Execute("INSERT INTO libraries (id, name, media_type, watched_folder, display_order) VALUES (1, 'Movies', 'movie', '/in', 1)");
        Execute("INSERT INTO libraries (id, name, media_type, watched_folder, display_order) VALUES (2, 'TV', 'tv', '/tv', 2)");
    }

    public void Dispose()
    {
        _database.ClearPool();
        _temp.Dispose();
    }

    private void Migrate() => Execute(SchemaMigrator.ReadMigrationSql(SchemaMigrator.Migrations.Single(migration => migration.Number == 29)));

    [Fact]
    public void Every_workflow_starts_with_the_space_performance_kept_free()
    {
        Execute("UPDATE operator_settings SET minimum_free_disk_space_mb = 20480 WHERE id = 1");

        Migrate();

        Assert.Equal(20480L, Scalar("SELECT minimum_free_disk_space_mb FROM libraries WHERE id = 1"));
        Assert.Equal(20480L, Scalar("SELECT minimum_free_disk_space_mb FROM libraries WHERE id = 2"));
    }

    [Fact]
    public void A_check_switched_off_in_performance_stays_off_for_every_workflow()
    {
        Execute("UPDATE operator_settings SET minimum_free_disk_space_mb = 0 WHERE id = 1");

        Migrate();

        Assert.Equal(0L, Scalar("SELECT minimum_free_disk_space_mb FROM libraries WHERE id = 1"));
    }

    [Fact]
    public void Without_a_performance_row_the_workflows_take_the_default()
    {
        Execute("DELETE FROM operator_settings");

        Migrate();

        Assert.Equal(5120L, Scalar("SELECT minimum_free_disk_space_mb FROM libraries WHERE id = 1"));
    }

    [Fact]
    public void A_workflow_added_afterwards_starts_at_the_default()
    {
        Migrate();

        Execute("INSERT INTO libraries (id, name, media_type, watched_folder, display_order) VALUES (3, 'Anime', 'tv', '/anime', 3)");

        Assert.Equal(5120L, Scalar("SELECT minimum_free_disk_space_mb FROM libraries WHERE id = 3"));
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
