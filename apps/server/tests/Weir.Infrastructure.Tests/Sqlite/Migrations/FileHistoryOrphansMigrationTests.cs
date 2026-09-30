using System.Globalization;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// Migration <c>0034_file_history_orphans.sql</c>: a file's history gains the mark that says when its file went, and every
/// existing history row starts unmarked, so no history is removed on the strength of a mark that was never made.
/// <para>Each test builds a database at the revision before the migration, adds a history row as an earlier version stored it,
/// and runs the migration's own SQL.</para>
/// </summary>
public sealed class FileHistoryOrphansMigrationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public FileHistoryOrphansMigrationTests()
    {
        _database = new SqliteDatabase(_temp.Join("weir.sqlite3"));
        new SchemaMigrator(_database).EnsureAtHead();
        Execute("DROP INDEX ix_file_logs_orphaned_at");
        Execute("ALTER TABLE file_logs DROP COLUMN orphaned_at");
        Execute("INSERT INTO file_logs (relative_path, recorded_at) VALUES ('old.mkv', '2020-01-01 00:00:00.000000')");
    }

    public void Dispose()
    {
        _database.ClearPool();
        _temp.Dispose();
    }

    private void Migrate() => Execute(SchemaMigrator.ReadMigrationSql(SchemaMigrator.Migrations.Single(migration => migration.Number == 34)));

    [Fact]
    public void An_existing_history_row_is_kept_and_starts_unmarked()
    {
        Migrate();

        Assert.Equal("old.mkv", ScalarText("SELECT relative_path FROM file_logs"));
        Assert.Equal(1, Scalar("SELECT count(*) FROM file_logs WHERE orphaned_at IS NULL"));
    }

    [Fact]
    public void The_mark_is_indexed_so_the_retention_task_finds_marked_rows_without_reading_every_row()
    {
        Migrate();

        Assert.Equal(1, Scalar("SELECT count(*) FROM sqlite_master WHERE type = 'index' AND name = 'ix_file_logs_orphaned_at'"));
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
