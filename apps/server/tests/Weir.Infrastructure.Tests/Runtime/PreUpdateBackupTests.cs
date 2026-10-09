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

/// <summary>The copy of Weir's data taken before a start updates the database, or before the tray applies an update (#951).</summary>
public sealed class PreUpdateBackupTests : IDisposable
{
    private const string Version = "1.2.3";
    private const string Lead = "Weir couldn't save a copy of its data before updating, so it didn't change anything: ";

    private readonly TempDirectory _home = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
    private readonly ListLogger<PreUpdateBackup> _logger = new();

    public void Dispose() => _home.Dispose();

    private string BackupDir => _home.Join("backups");

    private string Folder => PreUpdateBackup.FolderIn(BackupDir);

    private string DatabasePath => _home.Join("data", "weir.sqlite3");

    private PreUpdateBackup Backup(
        string version = Version,
        string? fromVersion = null,
        Func<string, long?>? freeSpace = null,
        Action<SqliteConnection, string>? copy = null,
        Action<string>? saving = null,
        string? backupDir = null)
    {
        var backup = new PreUpdateBackup(backupDir ?? BackupDir, _home.Path, version, _time, _logger, fromVersion, saving);
        if (freeSpace is not null)
        {
            backup.FreeSpaceOf = freeSpace;
        }

        if (copy is not null)
        {
            backup.CopyDatabase = copy;
        }

        return backup;
    }

    /// <summary>A database at the oldest revision this build starts from.</summary>
    private SqliteDatabase OldDatabase(string? path = null)
    {
        path ??= DatabasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var database = new SqliteDatabase(path);
        new SchemaMigrator(database).EnsureAtBaseline();
        return database;
    }

    private static SchemaStartupOutcome Upgrade(SqliteDatabase database, PreUpdateBackup backup) =>
        new SchemaMigrator(database).EnsureAtHead((connection, revision) => backup.Save(connection, revision));

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

    private static string Stamped(DateTimeOffset at, string version = Version, string revision = "0036") =>
        $"weir-{revision}-to-{version}-{at.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}";

    private string Stem => Stamped(_time.GetUtcNow());

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

        var outcome = Upgrade(database, Backup(fromVersion: "1.2.2"));

        Assert.Equal(SchemaStartupOutcome.Upgraded, outcome);
        var expected = Path.Join(Folder, "weir-0036-to-1.2.3-20261010T090000Z.db");
        var latest = PreUpdateBackup.Latest(BackupDir);
        Assert.Equal(new PreUpdateBackupFile(expected, new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero), "1.2.3", "1.2.2"), latest);
        Assert.Equal("0036_drop_pruner_tables", Revision(expected));
        using (var copy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = expected, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            copy.Open();
            using var read = copy.CreateCommand();
            read.CommandText = "SELECT text FROM operator_note";
            Assert.Equal("kept in the log", read.ExecuteScalar());
        }

        Assert.Equal(SchemaMigrator.HeadRevision, Revision(DatabasePath, "schema_version", "revision"));
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains(expected, StringComparison.Ordinal));
        database.ClearPool();
    }

    [Fact]
    public void The_settings_files_and_the_secrets_that_exist_are_copied_beside_the_database_and_the_others_are_not_made_up()
    {
        File.WriteAllText(_home.Join("lan-access"), "on");
        File.WriteAllText(_home.Join("update-settings.json"), "{\"mode\":\"auto\"}");
        File.WriteAllText(_home.Join("session.secret"), "session-secret");
        File.WriteAllText(_home.Join("credentials.secret"), "credentials-secret");
        var database = OldDatabase();

        Upgrade(database, Backup());
        database.ClearPool();

        Assert.Equal(
            [$"{Stem}.backup.json", $"{Stem}.credentials.secret", $"{Stem}.db", $"{Stem}.lan-access", $"{Stem}.session.secret", $"{Stem}.update-settings.json"],
            Names(Folder));
        Assert.Equal("on", File.ReadAllText(Path.Join(Folder, $"{Stem}.lan-access")));
        Assert.Equal("{\"mode\":\"auto\"}", File.ReadAllText(Path.Join(Folder, $"{Stem}.update-settings.json")));
        Assert.Equal("credentials-secret", File.ReadAllText(Path.Join(Folder, $"{Stem}.credentials.secret")));
        Assert.Equal("session-secret", File.ReadAllText(Path.Join(Folder, $"{Stem}.session.secret")));
    }

    [Fact]
    public void A_version_is_named_without_its_build_metadata()
    {
        var database = OldDatabase();

        Upgrade(database, Backup("1.0.0-rc.13+abc1234"));
        database.ClearPool();

        Assert.Contains("weir-0036-to-1.0.0-rc.13-20261010T090000Z.db", Names(Folder));
    }

    [Fact]
    public void A_copy_already_saved_for_this_revision_and_version_is_used_and_nothing_more_is_written()
    {
        var database = OldDatabase();
        var first = Backup(fromVersion: "1.2.2");
        Upgrade(database, first);
        database.ClearPool();
        var before = Names(Folder);
        var saved = PreUpdateBackup.Latest(BackupDir)!;

        // The server that starts after the update finds the running server's copy, a minute later and not knowing the old version.
        _time.Advance(TimeSpan.FromMinutes(1));
        var secondLogger = new ListLogger<PreUpdateBackup>();
        var second = new PreUpdateBackup(BackupDir, _home.Path, Version, _time, secondLogger);
        var reopened = new SqliteDatabase(DatabasePath);
        PreUpdateBackupFile again;
        using (var connection = reopened.Open())
        {
            again = second.Save(connection, "0036_drop_pruner_tables");
        }

        reopened.ClearPool();

        Assert.Equal(saved, again);
        Assert.Equal(before, Names(Folder));
        Assert.Contains(secondLogger.Entries, entry => entry.Message.StartsWith("A copy of Weir's data from before this update is already saved", StringComparison.Ordinal));
    }

    [Fact]
    public void A_saved_copy_that_no_longer_opens_at_its_revision_is_not_trusted_and_a_new_one_is_made()
    {
        var database = OldDatabase();
        Upgrade(database, Backup());
        database.ClearPool();
        var damaged = Path.Join(Folder, $"{Stem}.db");
        File.WriteAllText(damaged, "not a database");

        _time.Advance(TimeSpan.FromMinutes(1));
        // The first database went to head with its upgrade; this one is another at the baseline.
        var again = OldDatabase(_home.Join("again", "weir.sqlite3"));
        Upgrade(again, Backup());
        again.ClearPool();

        Assert.Contains(Stamped(_time.GetUtcNow()) + ".db", Names(Folder));
        Assert.Equal("0036_drop_pruner_tables", Revision(Path.Join(Folder, Stamped(_time.GetUtcNow()) + ".db")));
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

        var error = Assert.Throws<PreUpdateBackupException>(() => Upgrade(database, Backup()));
        database.ClearPool();

        Assert.StartsWith(Lead, error.Message, StringComparison.Ordinal);
        Assert.Equal(Lead + error.Reason, error.Message);
        Assert.True(error.Reason.Length > 0);
        Assert.Equal(before, SchemaSnapshot.Describe(DatabasePath));
        Assert.Equal("0036_drop_pruner_tables", Revision(DatabasePath));
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.NotEmpty(_logger.Exceptions);
    }

    [Fact]
    public void A_volume_without_the_room_is_refused_before_anything_is_written_and_says_how_much_is_needed()
    {
        var database = OldDatabase();
        database.ClearPool();
        var databaseBytes = new FileInfo(DatabasePath).Length;
        var before = SchemaSnapshot.Describe(DatabasePath);

        var error = Assert.Throws<PreUpdateBackupException>(() => Upgrade(database, Backup(freeSpace: _ => 1024)));
        database.ClearPool();

        var needed = PreUpdateBackupFailure.SizeText(databaseBytes * 120 / 100);
        Assert.Contains($"needs about {needed} free to hold the copy and has 1 MB", error.Reason, StringComparison.Ordinal);
        Assert.Contains("Free up space there, then try again.", error.Reason, StringComparison.Ordinal);
        Assert.Equal(before, SchemaSnapshot.Describe(DatabasePath));
        Assert.Empty(Directory.EnumerateFiles(Folder));
    }

    [Fact]
    public void A_disk_that_fills_up_during_the_copy_is_said_to_be_full_without_the_exceptions_own_text()
    {
        var database = OldDatabase();
        database.ClearPool();
        var before = SchemaSnapshot.Describe(DatabasePath);
        var full = new IOException("raw operating system text: no space left", unchecked((int)0x80070070));

        var error = Assert.Throws<PreUpdateBackupException>(() => Upgrade(database, Backup(copy: (_, path) =>
        {
            File.WriteAllText(path, "half a copy");
            throw full;
        })));
        database.ClearPool();

        Assert.EndsWith("is full. Free up some space there, then try again.", error.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("raw operating system text", error.Message, StringComparison.Ordinal);
        Assert.Same(full, error.InnerException);
        Assert.Equal(before, SchemaSnapshot.Describe(DatabasePath));
        Assert.Empty(Directory.EnumerateFiles(Folder));
    }

    [Fact]
    public void Whatever_goes_wrong_during_the_copy_is_a_failed_backup()
    {
        var database = OldDatabase();
        database.ClearPool();

        var error = Assert.Throws<PreUpdateBackupException>(() => Upgrade(database, Backup(copy: (_, _) => throw new InvalidOperationException("a bug"))));
        database.ClearPool();

        Assert.Equal("Something unexpected went wrong. The details are in Weir's log files.", error.Reason);
        Assert.Equal("0036_drop_pruner_tables", Revision(DatabasePath));
    }

    [Fact]
    public void A_failure_is_worded_by_what_the_person_can_do_about_it()
    {
        const string Permission = "Weir isn't allowed to write to";
        const string InUse = "Another program is using Weir's database or its backup folder";
        var cases = new (Exception Failure, string Expected)[]
        {
            (new UnauthorizedAccessException("denied"), Permission),
            (new SqliteException("readonly", 8), Permission),
            (new SqliteException("raw sqlite text 13", 13), "is full"),
            (new SqliteException("raw sqlite text 5", 5), InUse),
            (new IOException("in use", unchecked((int)0x80070020)), InUse),
            (new IOException("no space", 28), "is full"),
            (new NotEnoughRoomException(500L * 1024 * 1024, 100L * 1024 * 1024), "needs about 500 MB free to hold the copy and has 100 MB"),
            (new IOException("something else"), "Something unexpected went wrong"),
            (new InvalidOperationException("outer", new UnauthorizedAccessException("inner")), Permission),
        };

        foreach (var (failure, expected) in cases)
        {
            var reason = PreUpdateBackupFailure.Describe(failure, _home.Join("backups", "pre-update"));

            Assert.Contains(expected, reason, StringComparison.Ordinal);
            Assert.DoesNotContain(failure.Message, reason, StringComparison.Ordinal);
        }
    }

    [WindowsFact("Drives have letters on Windows only.")]
    public void A_full_drive_is_named_on_windows()
    {
        var reason = PreUpdateBackupFailure.Describe(new IOException("x", 28), @"D:\Weir\backups\pre-update");

        Assert.Equal("Drive D: is full. Free up some space there, then try again.", reason);
    }

    [Fact]
    public void The_copy_is_announced_before_it_starts_with_the_size_of_the_database()
    {
        var database = OldDatabase();
        var sentences = new List<string>();
        var announcedBeforeCopy = false;

        Upgrade(database, Backup(
            saving: sentences.Add,
            copy: (connection, path) =>
            {
                announcedBeforeCopy = sentences.Count == 1 && _logger.Entries.Any(entry => entry.Message.StartsWith("Saving a copy of Weir's data (", StringComparison.Ordinal));
                using var command = connection.CreateCommand();
                command.CommandText = "VACUUM INTO $path";
                command.Parameters.AddWithValue("$path", path);
                command.ExecuteNonQuery();
            }));
        database.ClearPool();

        Assert.True(announcedBeforeCopy);
        Assert.Matches(@"^Saving a copy of Weir's data \(\d+(\.\d)? (MB|GB)\) before updating…$", Assert.Single(sentences));
    }

    [Fact]
    public void The_oldest_copy_is_pruned_before_the_new_one_is_written_so_there_are_never_more_than_five()
    {
        Directory.CreateDirectory(Folder);
        foreach (var day in Enumerable.Range(1, 5))
        {
            File.WriteAllText(Path.Join(Folder, Stamped(new DateTimeOffset(2026, 9, day, 8, 0, 0, TimeSpan.Zero)) + ".db"), "old copy");
        }

        var database = OldDatabase();
        var countWhileCopying = -1;

        Upgrade(database, Backup(copy: (connection, path) =>
        {
            countWhileCopying = Directory.EnumerateFiles(Folder, "*.db").Count();
            using var command = connection.CreateCommand();
            command.CommandText = "VACUUM INTO $path";
            command.Parameters.AddWithValue("$path", path);
            command.ExecuteNonQuery();
        }));
        database.ClearPool();

        Assert.Equal(PreUpdateBackup.MaxKept - 1, countWhileCopying);
        Assert.Equal(PreUpdateBackup.MaxKept, Directory.EnumerateFiles(Folder, "*.db").Count());
    }

    [Fact]
    public void Only_the_newest_five_copies_are_kept_and_only_names_this_class_writes_are_touched()
    {
        Directory.CreateDirectory(Folder);
        var older = Enumerable.Range(1, 6).Select(day => new DateTimeOffset(2026, 9, day, 8, 0, 0, TimeSpan.Zero)).ToList();
        foreach (var at in older)
        {
            File.WriteAllText(Path.Join(Folder, Stamped(at) + ".db"), "old copy");
            File.WriteAllText(Path.Join(Folder, Stamped(at) + ".lan-access"), "off");
            File.WriteAllText(Path.Join(Folder, Stamped(at) + ".session.secret"), "secret");
            File.WriteAllText(Path.Join(Folder, Stamped(at) + ".backup.json"), "{}");
        }

        // Names that only look like ours: somebody else's, never touched.
        var theirs = new[]
        {
            "notes.txt",
            "weir-notes.db",
            "weir-0036-to-1.2.3-20260101.db",
            "weir-36-to-1.2.3-20260101T000000Z.db",
            "weir-0036-to--20260101T000000Z.db",
            "weir-0036-to-1.2.3-20260101T000000Z.db.bak",
            "weir-0036-to-1.2.3-20260101T000000Z.db.partial.old",
            "my-weir-0036-to-1.2.3-20260101T000000Z.db",
            "weir-0036-to-1.2.3-20260101T000000Z.other",
        };
        foreach (var name in theirs)
        {
            File.WriteAllText(Path.Join(Folder, name), "mine");
        }

        File.WriteAllText(Path.Join(Folder, "weir-0036-to-1.2.3-20260801T000000Z.db.partial"), "crashed");
        File.WriteAllText(Path.Join(BackupDir, "weir-0036-to-1.2.3-20260101T000000Z.db"), "outside the folder");
        var database = OldDatabase();

        Upgrade(database, Backup());
        database.ClearPool();

        var kept = Names(Folder).Where(name => name.EndsWith(".db", StringComparison.Ordinal) && !theirs.Contains(name)).ToList();
        Assert.Equal(PreUpdateBackup.MaxKept, kept.Count);
        Assert.Contains(Stem + ".db", kept);
        Assert.DoesNotContain(Stamped(older[0]) + ".db", kept);
        Assert.DoesNotContain(Stamped(older[1]) + ".db", kept);
        Assert.Contains(Stamped(older[2]) + ".db", kept);
        foreach (var kind in new[] { "lan-access", "session.secret", "backup.json" })
        {
            Assert.False(File.Exists(Path.Join(Folder, $"{Stamped(older[0])}.{kind}")), kind);
            Assert.True(File.Exists(Path.Join(Folder, $"{Stamped(older[2])}.{kind}")), kind);
        }

        Assert.False(File.Exists(Path.Join(Folder, "weir-0036-to-1.2.3-20260801T000000Z.db.partial")));
        foreach (var name in theirs)
        {
            Assert.Equal("mine", File.ReadAllText(Path.Join(Folder, name)));
        }

        Assert.Equal("outside the folder", File.ReadAllText(Path.Join(BackupDir, "weir-0036-to-1.2.3-20260101T000000Z.db")));
    }

    [Fact]
    public void No_copy_is_taken_for_a_new_database_or_one_already_at_this_revision()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        var database = new SqliteDatabase(DatabasePath);
        var backup = Backup();

        Assert.Equal(SchemaStartupOutcome.Created, Upgrade(database, backup));
        Assert.Equal(SchemaStartupOutcome.AlreadyCurrent, Upgrade(database, backup));
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

        var latest = PreUpdateBackup.Latest(BackupDir);

        Assert.Equal(Path.Join(Folder, "weir-0036-to-1.2.3-20260905T080000Z.db"), latest?.DatabasePath);
        Assert.Equal("1.2.3", latest?.ToVersion);
        Assert.Null(latest?.FromVersion);
    }

    [PosixFact("POSIX permission bits do not exist on Windows; the copy there gets an explicit owner-only access list.")]
    [UnsupportedOSPlatform("windows")]
    public void The_copy_is_readable_by_its_owner_only()
    {
        File.WriteAllText(_home.Join("lan-access"), "on");
        File.WriteAllText(_home.Join("credentials.secret"), "credentials-secret");
        var database = OldDatabase();

        Upgrade(database, Backup());
        database.ClearPool();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Folder));
        foreach (var file in Directory.EnumerateFiles(Folder))
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }
    }

    [WindowsFact("Access control lists exist on Windows only.")]
    [SupportedOSPlatform("windows")]
    public void On_windows_the_folder_and_every_file_get_an_owner_only_access_list_even_outside_the_data_folder()
    {
        // The backup folder is not under the data folder, so nothing the tray did to the data folder reaches it.
        using var elsewhere = new TempDirectory();
        File.WriteAllText(_home.Join("session.secret"), "session-secret");
        File.WriteAllText(_home.Join("credentials.secret"), "credentials-secret");
        File.WriteAllText(_home.Join("lan-access"), "on");
        var database = OldDatabase();

        Upgrade(database, Backup(backupDir: elsewhere.Join("copies")));
        database.ClearPool();

        var folder = PreUpdateBackup.FolderIn(elsewhere.Join("copies"));
        Assert.True(OwnerOnlyAccess.IsOwnerOnly(new DirectoryInfo(folder).GetAccessControl()));
        var files = Directory.EnumerateFiles(folder).ToList();
        Assert.Contains(files, file => file.EndsWith(".credentials.secret", StringComparison.Ordinal));
        Assert.Contains(files, file => file.EndsWith(".db", StringComparison.Ordinal));
        foreach (var file in files)
        {
            Assert.True(OwnerOnlyAccess.IsOwnerOnly(new FileInfo(file).GetAccessControl()), file);
        }
    }
}
