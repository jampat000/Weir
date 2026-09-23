using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// #652's migration (<c>0015_handback_outcomes.sql</c>): the hand-back record, the manager's outcome on a hand-off, and the
/// unclaimed hand-back cleanup, which an existing install gets switched off with a 14-day wait.
/// </summary>
public sealed class HandbackOutcomesMigrationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public HandbackOutcomesMigrationTests()
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
    public void An_existing_settings_row_gets_the_cleanup_switched_off_with_a_fourteen_day_wait()
    {
        Execute("INSERT OR IGNORE INTO operator_settings (id) VALUES (1)");

        Assert.Equal(0L, Scalar("SELECT unclaimed_handback_cleanup_enabled FROM operator_settings WHERE id = 1"));
        Assert.Equal(14L, Scalar("SELECT unclaimed_handback_window_days FROM operator_settings WHERE id = 1"));
        Assert.Equal(1L, Scalar("SELECT unclaimed_handback_cleanup_interval_seconds IS NULL FROM operator_settings WHERE id = 1"));
    }

    [Fact]
    public void A_hand_off_starts_with_no_outcome_and_one_copy_per_file_is_kept()
    {
        Execute("INSERT INTO libraries (id, name, media_type) VALUES (31, 'Films', 'movie')");
        Execute("INSERT INTO media_manager_handoffs (source_key, handoff_id, library_id, relative_path) VALUES ('deluno', 'h1', 31, 'Film/film.mkv')");
        Execute("INSERT INTO handbacks (library_id, relative_path, output_path, output_size, output_mtime_ns, written_at) VALUES (31, 'Film/film.mkv', '/out/film.mkv', 5, 7, '2026-09-23 10:00:00')");

        Assert.Equal(1L, Scalar("SELECT count(*) FROM media_manager_handoffs WHERE outcome IS NULL AND outcome_released = 0 AND pending_report_json IS NULL AND download_id IS NULL"));
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            Execute("INSERT INTO handbacks (library_id, relative_path, output_path, output_size, output_mtime_ns, written_at) VALUES (31, 'Film/film.mkv', '/out/b.mkv', 1, 1, '2026-09-23 10:00:00')"));

        // A library that goes takes its copies' records with it.
        Execute("PRAGMA foreign_keys = ON; DELETE FROM libraries WHERE id = 31");
        Assert.Equal(0L, Scalar("SELECT count(*) FROM handbacks"));
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
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
