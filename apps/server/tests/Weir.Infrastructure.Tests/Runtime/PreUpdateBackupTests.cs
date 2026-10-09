using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Weir.Infrastructure.Runtime;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Media;
using Weir.Infrastructure.Tests.Sqlite;

namespace Weir.Infrastructure.Tests.Runtime;

/// <summary>The copy of Weir's data taken before a start updates the database (#951).</summary>
public sealed class PreUpdateBackupTests : IDisposable
{
    private const string Version = "1.2.3";

    private readonly TempDirectory _home = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
    private readonly ListLogger<PreUpdateBackup> _logger = new();

    public void Dispose() => _home.Dispose();

    private string BackupDir => _home.Join("backups");

    private string Folder => PreUpdateBackup.FolderIn(BackupDir);

    private string DatabasePath => _home.Join("data", "weir.sqlite3");

    private PreUpdateBackup Backup(string version = Version) => new(BackupDir, _home.Path, version, _time, _logger);

    /// <summary>A database at the oldest revision this build starts from, holding one row only that revision's data would have.</summary>
    private SqliteDatabase OldDatabase()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        var database = new SqliteDatabase(DatabasePath);
        new SchemaMigrator(database).EnsureAtBaseline();
        return database;
    }

    private static string Revision(string path, string table = "alembic_version", string column = "version_num")
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM {table}";
        return (string)command.ExecuteScalar()!;
    }

    private static string[] Names(string folder) =>
        [.. Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).Select(name => name!).Order(StringComparer.Ordinal)];

    private string Stamped(DateTimeOffset at, string version = Version, string revision = "0036") =>
        $"weir-{revision}-to-{version}-{at.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}";

    [Fact]
    public void A_database_at_an_older_revision_is_copied_before_it_is_migrated_and_the_copy_opens_at_the_old_revision()
    {
        var database = OldDatabase();
        // Written through a pooled connection that stays open, so the row is in the write-ahead log and not yet in the database file.
        using var writer = database.Open();
        using (var write = writer.CreateCommand())
        {
            write.CommandText = "CREATE TABLE operator_note (text TEXT); INSERT INTO operator_note VALUES ('kept in the log');";
            write.ExecuteNonQuery();
        }

        var outcome = new SchemaMigrator(database).EnsureAtHead(Backup().Take);

        Assert.Equal(SchemaStartupOutcome.Upgraded, outcome);
        var expected = Path.Join(Folder, "weir-0036-to-1.2.3-20261010T090000Z.db");
        Assert.Equal(expected, PreUpdateBackup.Latest(BackupDir)?.DatabasePath);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero), PreUpdateBackup.Latest(BackupDir)?.TakenAt);
        Assert.Equal("0036_drop_pruner_tables", Revision(expected));
        using (var copy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = expected, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            copy.Open();
            using var read = copy.CreateCommand();
            read.CommandText = "SELECT text FROM operator_note";
            Assert.Equal("kept in the log", read.ExecuteScalar());
        }

        Assert.Equal(SchemaMigrator.HeadRevision, Revision(DatabasePath, "schema_version", "revision"));
        Assert.Equal([Path.GetFileName(expected)], Names(Folder));
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains(expected, entry.Message, StringComparison.Ordinal);
        database.ClearPool();
    }

    [Fact]
    public void The_settings_files_that_exist_are_copied_beside_the_database_and_the_others_are_not_made_up()
    {
        File.WriteAllText(_home.Join("lan-access"), "on");
        File.WriteAllText(_home.Join("update-settings.json"), "{\"mode\":\"auto\"}");
        var database = OldDatabase();

        new SchemaMigrator(database).EnsureAtHead(Backup().Take);
        database.ClearPool();

        var stem = Stamped(_time.GetUtcNow());
        Assert.Equal([$"{stem}.db", $"{stem}.lan-access", $"{stem}.update-settings.json"], Names(Folder));
        Assert.Equal("on", File.ReadAllText(Path.Join(Folder, $"{stem}.lan-access")));
        Assert.Equal("{\"mode\":\"auto\"}", File.ReadAllText(Path.Join(Folder, $"{stem}.update-settings.json")));
    }

    [Fact]
    public void A_version_is_named_without_its_build_metadata()
    {
        var database = OldDatabase();

        new SchemaMigrator(database).EnsureAtHead(Backup("1.0.0-rc.13+abc1234").Take);
        database.ClearPool();

        Assert.Equal(["weir-0036-to-1.0.0-rc.13-20261010T090000Z.db"], Names(Folder));
    }

    [Fact]
    public void When_the_copy_cannot_be_made_nothing_is_migrated_and_the_reason_is_said_plainly()
    {
        var database = OldDatabase();
        database.ClearPool();
        var before = SchemaSnapshot.Describe(DatabasePath);
        // A file where the pre-update folder belongs.
        Directory.CreateDirectory(BackupDir);
        File.WriteAllText(Folder, "in the way");

        var error = Assert.Throws<PreUpdateBackupException>(() => new SchemaMigrator(database).EnsureAtHead(Backup().Take));
        database.ClearPool();

        Assert.StartsWith(
            "Weir couldn't save a copy of its data before updating, so it didn't change anything: ",
            error.Message,
            StringComparison.Ordinal);
        Assert.True(error.Message.Length > "Weir couldn't save a copy of its data before updating, so it didn't change anything: ".Length);
        Assert.Equal(before, SchemaSnapshot.Describe(DatabasePath));
        Assert.Equal("0036_drop_pruner_tables", Revision(DatabasePath));
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void A_copy_that_fails_part_way_leaves_nothing_behind_and_the_database_unmigrated()
    {
        var database = OldDatabase();
        database.ClearPool();
        // lan-access exists as a file when checked, but its copy target is taken: the database copy has been made by then.
        File.WriteAllText(_home.Join("lan-access"), "off");
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Join(Folder, $"{Stamped(_time.GetUtcNow())}.lan-access"), "someone else's");

        Assert.Throws<PreUpdateBackupException>(() => new SchemaMigrator(database).EnsureAtHead(Backup().Take));
        database.ClearPool();

        Assert.Equal("0036_drop_pruner_tables", Revision(DatabasePath));
        Assert.Equal([$"{Stamped(_time.GetUtcNow())}.lan-access"], Names(Folder));
        Assert.Equal("someone else's", File.ReadAllText(Path.Join(Folder, $"{Stamped(_time.GetUtcNow())}.lan-access")));
    }

    [Fact]
    public void Only_the_newest_five_copies_are_kept_and_nothing_else_in_the_folder_is_touched()
    {
        Directory.CreateDirectory(Folder);
        var older = Enumerable.Range(1, 6).Select(day => new DateTimeOffset(2026, 9, day, 8, 0, 0, TimeSpan.Zero)).ToList();
        foreach (var at in older)
        {
            File.WriteAllText(Path.Join(Folder, Stamped(at) + ".db"), "old copy");
            File.WriteAllText(Path.Join(Folder, Stamped(at) + ".lan-access"), "off");
        }

        File.WriteAllText(Path.Join(Folder, "weir-0036-to-1.2.3-20260801T000000Z.db.partial"), "crashed");
        File.WriteAllText(Path.Join(Folder, "notes.txt"), "mine");
        File.WriteAllText(Path.Join(Folder, "weir-notes.db"), "mine too");
        File.WriteAllText(Path.Join(BackupDir, "weir-0036-to-1.2.3-20260101T000000Z.db"), "outside the folder");
        var database = OldDatabase();

        new SchemaMigrator(database).EnsureAtHead(Backup().Take);
        database.ClearPool();

        var kept = Names(Folder).Where(name => name.EndsWith(".db", StringComparison.Ordinal) && name != "weir-notes.db").ToList();
        Assert.Equal(PreUpdateBackup.MaxKept, kept.Count);
        Assert.Contains(Stamped(_time.GetUtcNow()) + ".db", kept);
        Assert.DoesNotContain(Stamped(older[0]) + ".db", kept);
        Assert.DoesNotContain(Stamped(older[1]) + ".db", kept);
        Assert.Contains(Stamped(older[2]) + ".db", kept);
        Assert.False(File.Exists(Path.Join(Folder, Stamped(older[0]) + ".lan-access")));
        Assert.True(File.Exists(Path.Join(Folder, Stamped(older[2]) + ".lan-access")));
        Assert.False(File.Exists(Path.Join(Folder, "weir-0036-to-1.2.3-20260801T000000Z.db.partial")));
        Assert.Equal("mine", File.ReadAllText(Path.Join(Folder, "notes.txt")));
        Assert.Equal("mine too", File.ReadAllText(Path.Join(Folder, "weir-notes.db")));
        Assert.Equal("outside the folder", File.ReadAllText(Path.Join(BackupDir, "weir-0036-to-1.2.3-20260101T000000Z.db")));
    }

    [Fact]
    public void No_copy_is_taken_for_a_new_database_or_one_already_at_this_revision()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        var database = new SqliteDatabase(DatabasePath);
        var backup = Backup();

        Assert.Equal(SchemaStartupOutcome.Created, new SchemaMigrator(database).EnsureAtHead(backup.Take));
        Assert.Equal(SchemaStartupOutcome.AlreadyCurrent, new SchemaMigrator(database).EnsureAtHead(backup.Take));
        database.ClearPool();

        Assert.False(Directory.Exists(Folder));
        Assert.Empty(_logger.Entries);
        Assert.Null(PreUpdateBackup.Latest(BackupDir));
    }

    [Fact]
    public void The_latest_copy_is_the_one_taken_last_and_there_is_none_before_the_first()
    {
        Assert.Null(PreUpdateBackup.Latest(BackupDir));
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Join(Folder, Stamped(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero)) + ".db"), "older");
        File.WriteAllText(Path.Join(Folder, Stamped(new DateTimeOffset(2026, 9, 5, 8, 0, 0, TimeSpan.Zero)) + ".db"), "newer");
        File.WriteAllText(Path.Join(Folder, Stamped(new DateTimeOffset(2026, 9, 9, 8, 0, 0, TimeSpan.Zero)) + ".lan-access"), "off");

        Assert.Equal(Path.Join(Folder, "weir-0036-to-1.2.3-20260905T080000Z.db"), PreUpdateBackup.Latest(BackupDir)?.DatabasePath);
    }

    [PosixFact("POSIX permission bits do not exist on Windows; the copy there inherits the data folder's owner-only access list.")]
    [UnsupportedOSPlatform("windows")]
    public void The_copy_is_readable_by_its_owner_only()
    {
        File.WriteAllText(_home.Join("lan-access"), "on");
        var database = OldDatabase();

        new SchemaMigrator(database).EnsureAtHead(Backup().Take);
        database.ClearPool();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Folder));
        foreach (var file in Directory.EnumerateFiles(Folder))
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }
    }
}
