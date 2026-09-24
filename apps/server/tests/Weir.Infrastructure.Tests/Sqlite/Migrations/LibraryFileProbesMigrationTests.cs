using Weir.Infrastructure.LibraryMode;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// #715's migration (<c>0019_library_file_probes.sql</c>): each file's probe document moves from <c>library_files</c> into
/// <c>library_file_probes</c>, and an existing install keeps every document it had. Each test starts at the frozen baseline
/// and seeds a completed library scan, so the rows arrive the way a real upgrade delivers them.
/// </summary>
public sealed class LibraryFileProbesMigrationTests : IDisposable
{
    private const string FilmProbe = """{"streams": [{"index": 0, "codec_type": "video", "codec_name": "hevc", "width": 3840, "height": 2160}]}""";

    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public LibraryFileProbesMigrationTests()
    {
        _database = new SqliteDatabase(_temp.Join("weir.sqlite3"));
        Assert.Equal(SchemaStartupOutcome.Created, new SchemaMigrator(_database).EnsureAtBaseline());
        Execute("DELETE FROM refiner_libraries");
        Execute(
            "INSERT INTO refiner_libraries (id, name, media_type, watched_folder, output_folder, work_folder, display_order) " +
            "VALUES (1, 'Movies library', 'movie', '/in', '/out', '/work', 1)");
        Execute(
            "INSERT INTO refiner_jobs (dedupe_key, job_kind, payload_json, status, created_at, updated_at) " +
            "VALUES ('refiner.library.scan.v1:1:seed', 'refiner.library.scan.v1', @payload, 'completed', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)",
            ("@payload",
                "{\"library_id\": 1, \"ok\": true, \"scan_result\": {\"generated_at\": 1700000000, \"errors\": [], \"files\": [" +
                "{\"path\": \"/lib/film.mkv\", \"size_bytes\": 1000, \"mtime\": 1700000000, \"classification\": \"matches\", " +
                "\"probe_json\": " + System.Text.Json.JsonSerializer.Serialize(FilmProbe) + "}, " +
                "{\"path\": \"/lib/unread.mkv\", \"size_bytes\": 2000, \"mtime\": 1700000000, \"classification\": \"cannot_process\", \"probe_json\": null}" +
                "]}}"));
    }

    public void Dispose()
    {
        _database.ClearPool();
        _temp.Dispose();
    }

    [Fact]
    public async Task A_scanned_files_probe_document_moves_to_its_own_table_unchanged()
    {
        Upgrade();

        await using var uow = await UnitOfWork.OpenAsync(_database);
        var files = (await LibraryScanStore.CurrentFilesAsync(uow, 1)).ToDictionary(file => file.Path);
        Assert.Equal(FilmProbe, files["/lib/film.mkv"].ProbeJson);
        Assert.Null(files["/lib/unread.mkv"].ProbeJson);
        Assert.Equal(1L, Scalar("SELECT count(*) FROM library_file_probes"));
    }

    [Fact]
    public void The_file_rows_no_longer_carry_the_document()
    {
        Upgrade();

        Assert.Equal(0L, Scalar("SELECT count(*) FROM pragma_table_info('library_files') WHERE name = 'probe_json'"));
        Assert.Equal(2L, Scalar("SELECT count(*) FROM library_files"));
    }

    [Fact]
    public void Removing_a_file_row_removes_its_probe_document()
    {
        Upgrade();

        Execute("PRAGMA foreign_keys = ON; DELETE FROM library_files WHERE path = '/lib/film.mkv'");

        Assert.Equal(0L, Scalar("SELECT count(*) FROM library_file_probes"));
    }

    private void Upgrade() => Assert.Equal(SchemaStartupOutcome.Upgraded, new SchemaMigrator(_database).EnsureAtHead());

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

    private long Scalar(string sql)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
