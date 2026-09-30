using System.Globalization;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// Migration <c>0033_remove_failed_download_cleanup_jobs.sql</c>: work still queued for the removed failed-download cleanup is
/// dropped, and everything else in the queue, finished cleanup rows included, stays.
/// <para>Each test builds a database at head, adds job rows as an earlier version stored them, and runs the migration's own SQL.</para>
/// </summary>
public sealed class RemoveFailedDownloadCleanupJobsMigrationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public RemoveFailedDownloadCleanupJobsMigrationTests()
    {
        _database = new SqliteDatabase(_temp.Join("weir.sqlite3"));
        new SchemaMigrator(_database).EnsureAtHead();
    }

    public void Dispose()
    {
        _database.ClearPool();
        _temp.Dispose();
    }

    private void Migrate() => Execute(SchemaMigrator.ReadMigrationSql(SchemaMigrator.Migrations.Single(migration => migration.Number == 33)));

    private void Job(int id, string kind, string status) =>
        Execute($"INSERT INTO jobs (id, dedupe_key, job_kind, payload_json, status) VALUES ({id}, 'key{id}', '{kind}', '{{}}', '{status}')");

    [Fact]
    public void Queued_and_running_work_of_the_removed_cleanup_is_dropped()
    {
        Job(1, "processing.movie_failure_cleanup_sweep.v1", "pending");
        Job(2, "processing.tv_failure_cleanup_sweep.v1", "leased");

        Migrate();

        Assert.Equal(0, Scalar("SELECT count(*) FROM jobs"));
    }

    [Fact]
    public void Finished_work_of_the_removed_cleanup_stays_until_job_retention_removes_it()
    {
        Job(1, "processing.movie_failure_cleanup_sweep.v1", "completed");
        Job(2, "processing.tv_failure_cleanup_sweep.v1", "failed");

        Migrate();

        Assert.Equal(2, Scalar("SELECT count(*) FROM jobs"));
    }

    [Fact]
    public void Other_queued_work_is_untouched()
    {
        Job(1, "processing.work_temp_stale_sweep.v1", "pending");
        Job(2, "processing.file.remux_pass.v1", "leased");

        Migrate();

        Assert.Equal(2, Scalar("SELECT count(*) FROM jobs"));
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
