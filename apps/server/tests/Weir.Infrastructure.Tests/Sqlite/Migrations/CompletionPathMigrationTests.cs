using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// #708's part of migration <c>0017</c>: a completed pass recorded without its <c>relative_path</c> gets it from its detail, so
/// the scan's indexed lookup of earlier completions finds every one the old search of the detail text found.
/// </summary>
public sealed class CompletionPathMigrationTests : IDisposable
{
    private const string CompletedType = "processing.file_remux_pass_completed";

    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public CompletionPathMigrationTests()
    {
        _database = new SqliteDatabase(_temp.Join("weir.sqlite3"));
        Assert.Equal(SchemaStartupOutcome.Created, new SchemaMigrator(_database).EnsureAtBaseline());
    }

    public void Dispose()
    {
        _database.ClearPool();
        _temp.Dispose();
    }

    [Fact]
    public void A_completion_recorded_without_its_path_gets_the_path_its_detail_names()
    {
        Insert(CompletedType, """{"ok": true, "relative_media_path": "  Film (2001)/Film.mkv "}""", relativePath: null);

        Upgrade();

        Assert.Equal("Film (2001)/Film.mkv", PathOf(CompletedType));
    }

    [Fact]
    public void A_path_already_recorded_and_other_events_are_left_as_they_are()
    {
        Insert(CompletedType, """{"ok": true, "relative_media_path": "New/Film.mkv"}""", relativePath: "Recorded/Film.mkv");
        Insert("processing.file_remux_pass_failed", """{"relative_media_path": "Failed/Film.mkv"}""", relativePath: null);
        Insert(CompletedType, "not json", relativePath: null);

        Upgrade();

        Assert.Equal(["Recorded/Film.mkv"], Paths(CompletedType));
        Assert.Equal([null], Paths("processing.file_remux_pass_failed"));
    }

    [Fact]
    public async Task The_scan_finds_a_completion_recorded_before_paths_were_stored()
    {
        var source = _temp.Join("watch", "Film", "Film.mkv");
        var output = _temp.Join("out", "Film", "Film.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllBytes(source, [1, 2, 3]);
        File.WriteAllBytes(output, [1]);
        Insert(
            CompletedType,
            System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["ok"] = true, ["relative_media_path"] = "Film/Film.mkv", ["media_scope"] = "movie", ["output_file"] = output,
                ["source_deleted_after_success"] = false, ["inspected_source_path"] = source, ["source_size_bytes"] = 3,
            }),
            relativePath: null);

        Upgrade();

        await using var uow = await UnitOfWork.OpenAsync(_database);
        Assert.True(await WatchedFolderScanOps.CompletedRemuxOutputExistsForRelativePathAsync(uow, "Film/Film.mkv", "movie", null, null, source));
    }

    private void Upgrade() => Assert.Equal(SchemaStartupOutcome.Upgraded, new SchemaMigrator(_database).EnsureAtHead());

    private void Insert(string eventType, string detail, string? relativePath)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO activity_events (module, event_type, title, detail, relative_path) VALUES ('processing', @type, 'x', @detail, @path)";
        command.Parameters.AddWithValue("@type", eventType);
        command.Parameters.AddWithValue("@detail", detail);
        command.Parameters.AddWithValue("@path", (object?)relativePath ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private string? PathOf(string eventType) => Assert.Single(Paths(eventType));

    private List<string?> Paths(string eventType)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT relative_path FROM activity_events WHERE event_type = @type AND detail LIKE '{%' ORDER BY id";
        command.Parameters.AddWithValue("@type", eventType);
        using var reader = command.ExecuteReader();
        var paths = new List<string?>();
        while (reader.Read())
        {
            paths.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
        }

        return paths;
    }
}
