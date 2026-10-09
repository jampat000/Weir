using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// #941's migration (<c>0041_handoff_skipped_extras.sql</c>): a hand-off target a pass skipped under the workflow's minimum size was
/// recorded <c>failed</c>, and the hand-off was reported failed because of it. The migration puts the target right and, when
/// nothing else is wrong with the hand-off, the hand-off with it.
/// </summary>
public sealed class HandoffSkippedExtrasMigrationTests : IDisposable
{
    private const long Mb = 1024 * 1024;
    private const string Minimum = "Skipped because this file is 15.6 MB, under the 50 MB minimum.";

    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;
    private readonly long _library;

    public HandoffSkippedExtrasMigrationTests()
    {
        _database = new SqliteDatabase(_temp.Join("weir.sqlite3"));
        new SchemaMigrator(_database).EnsureAtHead();
        _library = long.Parse(Scalar("SELECT MIN(id) FROM libraries"), System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        _database.ClearPool();
        _temp.Dispose();
    }

    [Fact]
    public void A_target_skipped_under_the_minimum_size_becomes_skipped_and_the_hand_off_reported_failed_for_it_is_completed()
    {
        Handoff(1, "failed", "failed", ("Film/film.mkv", "completed", 80, "Remux finished."), ("Film/Gallery.mkv", "failed", 15, Minimum));

        Migrate();

        Assert.Equal("skipped", TargetResult(1, "Film/Gallery.mkv"));
        Assert.Equal("completed", TargetResult(1, "Film/film.mkv"));
        Assert.Equal(("completed", "completed"), HandoffStates(1));
    }

    [Fact]
    public void A_target_that_failed_for_another_reason_stays_failed_and_so_does_its_hand_off()
    {
        Handoff(1, "failed", "failed", ("Film/film.mkv", "completed", 80, "Remux finished."), ("Film/Gallery.mkv", "failed", 15, "ffmpeg died."));

        Migrate();

        Assert.Equal("failed", TargetResult(1, "Film/Gallery.mkv"));
        Assert.Equal(("failed", "failed"), HandoffStates(1));
    }

    [Fact]
    public void A_skipped_file_as_big_as_a_delivered_one_leaves_the_hand_off_failed()
    {
        Handoff(1, "failed", "failed", ("Film/featurette.mkv", "completed", 80, "Remux finished."), ("Film/film.mkv", "failed", 90, Minimum));

        Migrate();

        Assert.Equal("skipped", TargetResult(1, "Film/film.mkv"));
        Assert.Equal(("failed", "failed"), HandoffStates(1));
    }

    [Fact]
    public void A_hand_off_with_nothing_delivered_or_a_size_not_known_stays_failed()
    {
        Handoff(1, "failed", "failed", ("Film/Gallery.mkv", "failed", 15, Minimum));
        Handoff(2, "failed", "failed", ("Other/film.mkv", "completed", 0, "Remux finished."), ("Other/Gallery.mkv", "failed", 15, Minimum));

        Migrate();

        Assert.Equal(("failed", "failed"), HandoffStates(1));
        Assert.Equal(("failed", "failed"), HandoffStates(2));
    }

    [Fact]
    public void A_hand_off_reported_completed_is_left_as_it_is_and_the_migration_can_run_again()
    {
        Handoff(1, "completed", "completed", ("Film/film.mkv", "completed", 80, "Remux finished."), ("Film/Gallery.mkv", "skipped", 15, Minimum));

        Migrate();
        Migrate();

        Assert.Equal("skipped", TargetResult(1, "Film/Gallery.mkv"));
        Assert.Equal(("completed", "completed"), HandoffStates(1));
    }

    private static string MigrationSql => SchemaMigrator.ReadMigrationSql(SchemaMigrator.Migrations.Single(m => m.Number == 41));

    private void Migrate() => Execute(MigrationSql);

    /// <summary>A hand-off of a release folder, its targets, and the sources' sizes, as an earlier release left them.</summary>
    private void Handoff(int id, string state, string reportedStatus, params (string Path, string Result, long SizeMb, string Message)[] targets)
    {
        Execute(
            $"INSERT INTO media_manager_handoffs (id, source_key, handoff_id, library_id, relative_path, state, reported_status) " +
            $"VALUES ({id}, 'deluno', 'h{id}', {_library}, 'Release{id}', '{state}', '{reportedStatus}')");
        foreach (var (path, result, sizeMb, message) in targets)
        {
            Execute(
                $"INSERT INTO files (library_id, relative_path, status, size_bytes) VALUES ({_library}, '{path}', 'processed', {sizeMb * Mb})");
            Execute(
                $"INSERT INTO media_manager_handoff_targets (handoff_row_id, relative_path, result, message) VALUES ({id}, '{path}', '{result}', '{message}')");
        }
    }

    private string TargetResult(int handoff, string path) =>
        Scalar($"SELECT result FROM media_manager_handoff_targets WHERE handoff_row_id = {handoff} AND relative_path = '{path}'");

    private (string State, string Reported) HandoffStates(int id) =>
        (Scalar($"SELECT state FROM media_manager_handoffs WHERE id = {id}"), Scalar($"SELECT reported_status FROM media_manager_handoffs WHERE id = {id}"));

    private void Execute(string sql)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private string Scalar(string sql)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
    }
}
