using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// <c>0042_file_skip_kind.sql</c>: a file skipped under the workflow's minimum size before skips had a code is given the code, so
/// nothing reads the sentence again. Each test starts at the frozen baseline and seeds the rows a real upgrade delivers.
/// </summary>
public sealed class FileSkipKindMigrationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public FileSkipKindMigrationTests()
    {
        _database = new SqliteDatabase(_temp.Join("weir.sqlite3"));
        Assert.Equal(SchemaStartupOutcome.Created, new SchemaMigrator(_database).EnsureAtBaseline());
        Execute("DELETE FROM refiner_libraries");
        Execute(
            "INSERT INTO refiner_libraries (id, name, media_type, watched_folder, output_folder, work_folder, display_order) " +
            "VALUES (1, 'Movies', 'movie', '/in', '/out', '/work', 1)");
        Seed("Film/Gallery.mkv", "skipped", "Skipped because this file is 15.6 MB, under the 50 MB minimum.");
        Seed("Film/Huge.mkv", "skipped", "Skipped because this file is 90.0 MB and exceeds the 50 MB workflow maximum.");
        Seed("Film/Old.mkv", "processed", "Skipped because this file is 15.6 MB, under the 50 MB minimum.");
    }

    public void Dispose()
    {
        _database.ClearPool();
        _temp.Dispose();
    }

    [Fact]
    public void A_file_skipped_under_the_minimum_size_is_given_its_code_and_no_other_file_is()
    {
        Assert.Equal(SchemaStartupOutcome.Upgraded, new SchemaMigrator(_database).EnsureAtHead());

        Assert.Equal("below_minimum_size", Text("SELECT skip_kind FROM files WHERE relative_path = 'Film/Gallery.mkv'"));
        Assert.Null(Text("SELECT skip_kind FROM files WHERE relative_path = 'Film/Huge.mkv'"));
        Assert.Null(Text("SELECT skip_kind FROM files WHERE relative_path = 'Film/Old.mkv'"));
    }

    private void Seed(string path, string status, string reason) =>
        Execute(
            "INSERT INTO refiner_files (library_id, relative_path, status, status_reason) VALUES (1, @path, @status, @reason)",
            ("@path", path), ("@status", status), ("@reason", reason));

    private void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    private string? Text(string sql)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() is string text ? text : null;
    }
}
