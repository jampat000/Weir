using System.Globalization;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// Migration <c>0021_keep_original_after_clean.sql</c> (#735): every existing library gets the setting off and no
/// originals folder, and every existing swap row gets no kept-original path, so an upgrade changes nothing.
/// </summary>
public sealed class KeepOriginalAfterCleanMigrationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public KeepOriginalAfterCleanMigrationTests()
    {
        _database = new SqliteDatabase(_temp.Join("weir.sqlite3"));
        new SchemaMigrator(_database).EnsureAtHead();
        Execute("DELETE FROM libraries");
    }

    public void Dispose()
    {
        _database.ClearPool();
        _temp.Dispose();
    }

    [Fact]
    public void A_fresh_database_has_the_setting_off_with_no_originals_folder()
    {
        Execute("INSERT INTO libraries (name, media_type, watched_folder, display_order) VALUES ('Movies', 'movie', '/in', 1)");

        Assert.Equal(0L, Scalar("SELECT keep_original_after_clean FROM libraries WHERE name = 'Movies'"));
        Assert.Equal(string.Empty, ScalarString("SELECT originals_folder FROM libraries WHERE name = 'Movies'"));
    }

    [Fact]
    public void A_library_swap_row_has_no_kept_original_path_until_one_is_recorded()
    {
        Execute("INSERT INTO jobs (dedupe_key, job_kind, payload_json) VALUES ('k', 'processing.library.clean.v1', '{}')");
        var jobId = Convert.ToInt64(Scalar("SELECT id FROM jobs WHERE dedupe_key = 'k'"), CultureInfo.InvariantCulture);
        Execute($"INSERT INTO library_swaps (job_id, state, original_path, temp_path, backup_path, committed) VALUES ({jobId}, 'writing', '/lib/a.mkv', '/lib/a.weir-tmp.mkv', '/lib/a.weir-bak.mkv', 0)");

        Assert.Equal(DBNull.Value, Scalar("SELECT kept_original_path FROM library_swaps WHERE job_id = " + jobId.ToString(CultureInfo.InvariantCulture)));
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

    private string ScalarString(string sql) => Convert.ToString(Scalar(sql), CultureInfo.InvariantCulture) ?? string.Empty;
}
