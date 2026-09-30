using System.Globalization;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// Migration <c>0032_failed_files_are_never_held.sql</c>: a file put on hold after repeated failures becomes a failed file
/// with the same reason, and a hold that carries no failures is left alone.
/// <para>Each test builds a database at head, adds rows as an earlier version stored them, and runs the migration's own SQL.</para>
/// </summary>
public sealed class FailedFilesAreNeverHeldMigrationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public FailedFilesAreNeverHeldMigrationTests()
    {
        _database = new SqliteDatabase(_temp.Join("weir.sqlite3"));
        new SchemaMigrator(_database).EnsureAtHead();
        Execute("DELETE FROM libraries");
        Execute("INSERT INTO libraries (id, name, media_type, watched_folder, display_order) VALUES (1, 'Movies', 'movie', '/in', 1)");
    }

    public void Dispose()
    {
        _database.ClearPool();
        _temp.Dispose();
    }

    private void Migrate() => Execute(SchemaMigrator.ReadMigrationSql(SchemaMigrator.Migrations.Single(migration => migration.Number == 32)));

    private void File(int id, string status, long attempts, string reason = "", string? holdUntil = null) =>
        Execute(
            "INSERT INTO files (id, library_id, relative_path, status, status_reason, failure_class, failure_attempts, hold_until) " +
            $"VALUES ({id}, 1, 'file{id}.mkv', '{status}', '{reason}', 'execution', {attempts}, {(holdUntil is null ? "NULL" : $"'{holdUntil}'")})");

    [Fact]
    public void A_file_held_after_repeated_failures_becomes_a_failed_file_with_its_reason_and_no_retry_owed()
    {
        File(1, "on_hold", 3, reason: "Weir held this file after 3 repeated execution failures.", holdUntil: "2026-09-30 12:00:00.000000");

        Migrate();

        Assert.Equal("processing_failed|Weir held this file after 3 repeated execution failures.||", ScalarText(
            "SELECT status || '|' || status_reason || '|' || coalesce(hold_until, '') || '|' || coalesce(next_retry_at, '') FROM files WHERE id = 1"));
        Assert.Equal(3, Scalar("SELECT failure_attempts FROM files WHERE id = 1"));
    }

    [Fact]
    public void A_hold_that_carries_no_failures_is_left_alone()
    {
        File(1, "on_hold", 0, reason: "Waiting for the file to settle.", holdUntil: "2026-09-30 12:00:00.000000");

        Migrate();

        Assert.Equal("on_hold", ScalarText("SELECT status FROM files WHERE id = 1"));
        Assert.Equal("2026-09-30 12:00:00.000000", ScalarText("SELECT hold_until FROM files WHERE id = 1"));
    }

    [Fact]
    public void A_file_that_already_failed_is_left_alone()
    {
        File(1, "processing_failed", 3, reason: "Weir tried this file 3 times and stopped.");

        Migrate();

        Assert.Equal("processing_failed|Weir tried this file 3 times and stopped.", ScalarText("SELECT status || '|' || status_reason FROM files WHERE id = 1"));
    }

    [Fact]
    public void A_new_install_has_no_file_that_is_held_after_failing()
    {
        Assert.Equal(0, Scalar("SELECT count(*) FROM files WHERE status = 'on_hold' AND failure_attempts >= 3"));
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
