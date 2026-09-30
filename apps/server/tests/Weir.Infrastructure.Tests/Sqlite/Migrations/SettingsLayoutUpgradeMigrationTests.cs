using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// The settings-layout migrations run one after another on a database the previous release wrote: each workflow's waits,
/// minimum size and free space, and Performance's old values, all land on the workflow together.
/// </summary>
public sealed class SettingsLayoutUpgradeMigrationTests
{
    /// <summary>The last migration the previous release shipped.</summary>
    private const int PreviousReleaseMigration = 28;

    [Fact]
    public void A_workflow_from_the_previous_release_keeps_its_waits_size_and_free_space_when_upgraded_to_head()
    {
        using var temp = new TempDirectory();
        var path = temp.Join("weir.sqlite3");
        var script = string.Concat(
            SchemaMigrator.Migrations.Where(m => m.Number <= PreviousReleaseMigration).OrderBy(m => m.Number).Select(SchemaMigrator.ReadMigrationSql)) +
            $"DELETE FROM alembic_version; INSERT INTO alembic_version (version_num) VALUES ('{SchemaMigrator.Migrations.Single(m => m.Number == PreviousReleaseMigration).Revision}');" +
            "UPDATE operator_settings SET min_file_age_seconds = 45, min_input_file_size_mb = 30, minimum_free_disk_space_mb = 20480 WHERE id = 1;" +
            "DELETE FROM libraries;" +
            "INSERT INTO libraries (id, name, media_type, watched_folder, display_order, min_file_age_seconds, hold_minutes, " +
            "file_detection_interval_seconds, min_file_size_mb) VALUES (1, 'Own settings', 'movie', '/in1', 1, 120, 5, 30, 200);" +
            "INSERT INTO libraries (id, name, media_type, watched_folder, display_order, min_file_age_seconds, hold_minutes, " +
            "file_detection_interval_seconds, min_file_size_mb) VALUES (2, 'Follows Performance', 'tv', '/in2', 2, NULL, 0, 30, NULL);";
        SchemaSnapshot.Execute(path, script);

        var database = new SqliteDatabase(path);
        var outcome = new SchemaMigrator(database).EnsureAtHead();
        database.ClearPool();

        Assert.Equal(SchemaStartupOutcome.Upgraded, outcome);
        Assert.Equal(420, Value(path, "ready_after_seconds", 1));
        Assert.Equal(200, Value(path, "min_file_size_mb", 1));
        Assert.Equal(45, Value(path, "ready_after_seconds", 2));
        Assert.Equal(30, Value(path, "min_file_size_mb", 2));
        Assert.Equal(20480, Value(path, "minimum_free_disk_space_mb", 1));
        Assert.Equal(20480, Value(path, "minimum_free_disk_space_mb", 2));
        Assert.Equal(0, SchemaSnapshot.ScalarLong(path, "SELECT COUNT(*) FROM libraries WHERE library_rule_set_id IS NOT NULL"));
    }

    private static long Value(string path, string column, int workflowId) =>
        SchemaSnapshot.ScalarLong(path, $"SELECT {column} FROM libraries WHERE id = {workflowId}");
}
