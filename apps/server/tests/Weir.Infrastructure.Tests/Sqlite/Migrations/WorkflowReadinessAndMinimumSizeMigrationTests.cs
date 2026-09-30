using System.Globalization;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// Migration <c>0029_workflow_readiness_and_minimum_size.sql</c>: a workflow's three waits become one, the longest it had, and
/// its minimum size becomes a value it always holds. Nothing is picked up sooner than before.
/// <para>Each test builds a database as it was before this migration, writes workflows in that shape, and runs the migration's
/// own SQL.</para>
/// </summary>
public sealed class WorkflowReadinessAndMinimumSizeMigrationTests : IDisposable
{
    private const int PreviousMigration = 28;
    private const int ThisMigration = 29;

    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public WorkflowReadinessAndMinimumSizeMigrationTests()
    {
        _database = new SqliteDatabase(_temp.Join("weir.sqlite3"));
        foreach (var migration in SchemaMigrator.Migrations.Where(m => m.Number <= PreviousMigration).OrderBy(m => m.Number))
        {
            Execute(SchemaMigrator.ReadMigrationSql(migration));
        }

        Execute("DELETE FROM libraries");
        SetPerformance(ageSeconds: 60, minSizeMb: 50);
    }

    public void Dispose()
    {
        _database.ClearPool();
        _temp.Dispose();
    }

    private void Migrate() =>
        Execute(SchemaMigrator.ReadMigrationSql(SchemaMigrator.Migrations.Single(migration => migration.Number == ThisMigration)));

    private void SetPerformance(long ageSeconds, long minSizeMb) =>
        Execute($"UPDATE operator_settings SET min_file_age_seconds = {ageSeconds}, min_input_file_size_mb = {minSizeMb} WHERE id = 1");

    private void Workflow(
        int id, string? ageSeconds = "60", long holdMinutes = 0, long sizeStableSeconds = 30, bool ignoreSizeChanges = false, string? minSizeMb = "50") =>
        Execute(
            "INSERT INTO libraries (id, name, media_type, watched_folder, display_order, min_file_age_seconds, hold_minutes, " +
            "file_detection_interval_seconds, ignore_size_changes, min_file_size_mb) " +
            $"VALUES ({id}, 'Workflow {id}', 'movie', '/in{id}', {id}, {ageSeconds ?? "NULL"}, {holdMinutes}, {sizeStableSeconds}, " +
            $"{(ignoreSizeChanges ? 1 : 0)}, {minSizeMb ?? "NULL"})");

    private long ReadyAfter(int id) => Scalar($"SELECT ready_after_seconds FROM libraries WHERE id = {id}");

    private long MinSize(int id) => Scalar($"SELECT min_file_size_mb FROM libraries WHERE id = {id}");

    [Fact]
    public void A_workflow_that_waited_on_its_own_age_keeps_it_when_the_size_wait_is_shorter()
    {
        Workflow(1, ageSeconds: "120", sizeStableSeconds: 30);

        Migrate();

        Assert.Equal(120, ReadyAfter(1));
    }

    [Fact]
    public void A_workflow_with_no_wait_of_its_own_gets_the_Performance_wait()
    {
        SetPerformance(ageSeconds: 300, minSizeMb: 50);
        Workflow(1, ageSeconds: null, sizeStableSeconds: 30);

        Migrate();

        Assert.Equal(300, ReadyAfter(1));
    }

    [Fact]
    public void A_hold_on_every_new_file_is_added_to_the_wait_after_a_change()
    {
        Workflow(1, ageSeconds: "60", holdMinutes: 5, sizeStableSeconds: 30);

        Migrate();

        Assert.Equal(360, ReadyAfter(1));
    }

    [Fact]
    public void A_hold_is_added_to_the_Performance_wait_for_a_workflow_that_followed_it()
    {
        SetPerformance(ageSeconds: 100, minSizeMb: 50);
        Workflow(1, ageSeconds: null, holdMinutes: 2, sizeStableSeconds: 30);

        Migrate();

        Assert.Equal(220, ReadyAfter(1));
    }

    [Fact]
    public void A_size_wait_longer_than_the_age_plus_hold_is_the_wait()
    {
        Workflow(1, ageSeconds: "10", holdMinutes: 0, sizeStableSeconds: 90);

        Migrate();

        Assert.Equal(90, ReadyAfter(1));
    }

    [Fact]
    public void A_workflow_that_waited_for_nothing_still_waits_for_nothing()
    {
        Workflow(1, ageSeconds: "0", holdMinutes: 0, sizeStableSeconds: 0);

        Migrate();

        Assert.Equal(0, ReadyAfter(1));
    }

    [Fact]
    public void A_workflow_that_ignored_size_changes_does_not_gain_a_size_wait()
    {
        Workflow(1, ageSeconds: "20", sizeStableSeconds: 300, ignoreSizeChanges: true);

        Migrate();

        Assert.Equal(20, ReadyAfter(1));
    }

    [Fact]
    public void Without_a_Performance_row_a_workflow_that_followed_it_gets_the_old_default_wait_and_size()
    {
        Execute("DELETE FROM operator_settings");
        Workflow(1, ageSeconds: null, sizeStableSeconds: 30, minSizeMb: null);

        Migrate();

        Assert.Equal(60, ReadyAfter(1));
        Assert.Equal(50, MinSize(1));
    }

    [Fact]
    public void The_wait_is_capped_at_what_a_workflow_can_hold()
    {
        Workflow(1, ageSeconds: "604800", holdMinutes: 20_000, sizeStableSeconds: 30);

        Migrate();

        Assert.Equal(1_209_600, ReadyAfter(1));
    }

    [Fact]
    public void A_minimum_size_a_workflow_set_is_kept()
    {
        SetPerformance(ageSeconds: 60, minSizeMb: 75);
        Workflow(1, minSizeMb: "200");

        Migrate();

        Assert.Equal(200, MinSize(1));
    }

    [Fact]
    public void A_workflow_that_followed_the_Performance_minimum_size_gets_its_value()
    {
        SetPerformance(ageSeconds: 60, minSizeMb: 75);
        Workflow(1, minSizeMb: null);

        Migrate();

        Assert.Equal(75, MinSize(1));
    }

    [Fact]
    public void A_minimum_size_of_zero_is_a_choice_and_is_kept()
    {
        SetPerformance(ageSeconds: 60, minSizeMb: 75);
        Workflow(1, minSizeMb: "0");

        Migrate();

        Assert.Equal(0, MinSize(1));
    }

    [Fact]
    public void Performance_no_longer_holds_either_setting()
    {
        Migrate();

        Assert.Empty(ColumnsOf("operator_settings").Intersect(["min_file_age_seconds", "min_input_file_size_mb"]));
    }

    [Fact]
    public void A_workflow_no_longer_holds_the_three_old_waits()
    {
        Migrate();

        Assert.Empty(ColumnsOf("libraries").Intersect(["min_file_age_seconds", "hold_minutes", "file_detection_interval_seconds"]));
        Assert.Contains("ready_after_seconds", ColumnsOf("libraries"));
    }

    [Fact]
    public void A_workflow_added_afterwards_starts_at_sixty_seconds_and_fifty_megabytes()
    {
        Migrate();

        Execute("INSERT INTO libraries (id, name, media_type, watched_folder, display_order) VALUES (1, 'New', 'movie', '/in', 1)");

        Assert.Equal(60, ReadyAfter(1));
        Assert.Equal(50, MinSize(1));
    }

    [Fact]
    public void A_workflow_is_otherwise_untouched()
    {
        Workflow(1);

        Migrate();

        Assert.Equal("Workflow 1", ScalarText("SELECT name FROM libraries WHERE id = 1"));
        Assert.Equal("/in1", ScalarText("SELECT watched_folder FROM libraries WHERE id = 1"));
    }

    private List<string> ColumnsOf(string table)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
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
