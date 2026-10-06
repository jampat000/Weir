using Microsoft.Data.Sqlite;
using Weir.Infrastructure.Auth;
using Weir.Infrastructure.Logging;
using Weir.Infrastructure.Scheduling;
using Weir.Infrastructure.Settings;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite;

public sealed class SchemaMigratorTests
{
    [Fact]
    public void A_database_at_the_baseline_is_upgraded_to_head()
    {
        // The baseline is the oldest revision this build starts from. Migrations were added past it,
        // so EnsureAtHead upgrades it in place instead of adopting it unchanged.
        using var temp = new TempDirectory();
        var path = temp.Join("weir.sqlite3");
        var baselineDatabase = new SqliteDatabase(path);
        new SchemaMigrator(baselineDatabase).EnsureAtBaseline();
        baselineDatabase.ClearPool();

        var database = new SqliteDatabase(path);
        var outcome = new SchemaMigrator(database).EnsureAtHead();
        database.ClearPool();

        Assert.Equal(SchemaStartupOutcome.Upgraded, outcome);
        Assert.Equal(1, SchemaSnapshot.ScalarLong(path, $"SELECT COUNT(*) FROM schema_version WHERE revision = '{SchemaMigrator.HeadRevision}'"));

        // Matches a database created fresh at head, once the seeded suite_settings row's timestamps
        // (masked by SchemaSnapshot) and the fact a fresh install seeds no libraries/rule sets are allowed for.
        using var freshTemp = new TempDirectory();
        var freshPath = freshTemp.Join("fresh.sqlite3");
        var freshDatabase = new SqliteDatabase(freshPath);
        Assert.Equal(SchemaStartupOutcome.Created, new SchemaMigrator(freshDatabase).EnsureAtHead());
        freshDatabase.ClearPool();
        Assert.Equal(SchemaSnapshot.Describe(freshPath), SchemaSnapshot.Describe(path));
    }

    [Fact]
    public void A_database_this_build_created_is_current_on_the_next_start()
    {
        using var temp = new TempDirectory();
        var database = new SqliteDatabase(temp.Join("weir.sqlite3"));
        Assert.Equal(SchemaStartupOutcome.Created, new SchemaMigrator(database).EnsureAtHead());
        Assert.Equal(SchemaStartupOutcome.AlreadyCurrent, new SchemaMigrator(database).EnsureAtHead());
        Assert.Equal(1, SchemaSnapshot.ScalarLong(database.DatabasePath, $"SELECT COUNT(*) FROM schema_version WHERE revision = '{SchemaMigrator.HeadRevision}'"));
        database.ClearPool();
    }

    [Fact]
    public void A_revision_from_before_the_baseline_is_not_a_weir_database_and_is_refused_untouched()
    {
        using var temp = new TempDirectory();
        var path = DatabaseStampedAtRevision(temp, "0035_direct_play_facts");
        var before = SchemaSnapshot.Describe(path);

        var database = new SqliteDatabase(path);
        var error = Assert.Throws<DatabaseSchemaMismatchException>(() => new SchemaMigrator(database).EnsureAtHead());
        database.ClearPool();

        Assert.Equal(SchemaMismatchKind.UnknownRevision, error.Kind);
        Assert.StartsWith("Database revision '0035_direct_play_facts' is not recognized by this Weir build", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, SchemaSnapshot.Describe(path));
    }

    [Fact]
    public void An_unknown_revision_is_refused_with_the_documented_message()
    {
        using var temp = new TempDirectory();
        var path = DatabaseStampedAtRevision(temp, "0099_from_the_future");

        var database = new SqliteDatabase(path);
        var error = Assert.Throws<DatabaseSchemaMismatchException>(() => new SchemaMigrator(database).EnsureAtHead());
        database.ClearPool();

        Assert.Equal(SchemaMismatchKind.UnknownRevision, error.Kind);
        Assert.Equal(
            $"Database revision '0099_from_the_future' is not recognized by this Weir build (expected head '{SchemaMigrator.HeadRevision}'). " +
            "The database may come from a newer release, or it was not created by Weir. Upgrade the application, restore a backup that matches this version, " +
            "or move this file aside so Weir creates a new one.",
            error.Message);
    }

    [Fact]
    public void Tables_without_a_recorded_revision_are_refused()
    {
        using var temp = new TempDirectory();
        var path = temp.Join("other.sqlite3");
        SchemaSnapshot.Execute(path, "CREATE TABLE something_else (id INTEGER PRIMARY KEY);");

        var database = new SqliteDatabase(path);
        var error = Assert.Throws<DatabaseSchemaMismatchException>(() => new SchemaMigrator(database).EnsureAtHead());
        database.ClearPool();

        Assert.Equal(SchemaMismatchKind.Unversioned, error.Kind);
        Assert.StartsWith(
            $"No schema revision is recorded for this database (Weir has not set it up). This build requires schema revision '{SchemaMigrator.HeadRevision}'.",
            error.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, SchemaSnapshot.ScalarLong(path, "SELECT COUNT(*) FROM sqlite_master"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_existing_file_without_a_schema_is_refused_and_left_empty(bool zeroBytes)
    {
        // Any unversioned database is refused; only a missing file is a new install.
        using var temp = new TempDirectory();
        var path = temp.Join("weir.sqlite3");
        if (zeroBytes)
        {
            File.WriteAllBytes(path, []);
        }
        else
        {
            SchemaSnapshot.Execute(path, "PRAGMA user_version = 0; VACUUM;");
        }

        var database = new SqliteDatabase(path);
        var error = Assert.Throws<DatabaseSchemaMismatchException>(() => new SchemaMigrator(database).EnsureAtHead());
        database.ClearPool();

        Assert.Equal(SchemaMismatchKind.Unversioned, error.Kind);
        Assert.StartsWith("No schema revision is recorded for this database", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, SchemaSnapshot.ScalarLong(path, "SELECT COUNT(*) FROM sqlite_master"));
        if (zeroBytes)
        {
            Assert.False(File.Exists(path + "-wal"));
        }
    }

    [Fact]
    public void A_missing_file_is_created_at_head()
    {
        using var temp = new TempDirectory();
        var path = temp.Join("weir.sqlite3");

        var database = new SqliteDatabase(path);
        Assert.Equal(SchemaStartupOutcome.Created, new SchemaMigrator(database).EnsureAtHead());
        database.ClearPool();

        Assert.Equal(1, SchemaSnapshot.ScalarLong(path, "SELECT COUNT(*) FROM schema_version"));
    }

    /// <summary>
    /// Two migrations sharing a number would silently overwrite one another's slot, and one listed out of order
    /// would run in the wrong sequence: <c>SchemaMigrator</c>'s decision of what to apply assumes both never happen.
    /// </summary>
    [Fact]
    public void Migration_numbers_are_strictly_increasing_and_unique()
    {
        var numbers = SchemaMigrator.Migrations.Select(migration => migration.Number).ToList();
        Assert.Equal(numbers.Distinct(), numbers);
        Assert.Equal(numbers.OrderBy(number => number), numbers);
    }

    /// <summary>
    /// Generates one case per revision from <see cref="SchemaMigrator.Migrations"/> instead of naming any
    /// migration: adding a new migration adds a case for it automatically and needs no edit here.
    /// </summary>
    public static IEnumerable<object[]> RevisionsBeforeHead() =>
        SchemaMigrator.Migrations.Take(SchemaMigrator.Migrations.Count - 1).Select(migration => new object[] { migration.Number });

    /// <summary>
    /// A database at any earlier revision upgrades to exactly the schema a fresh install gets at head, and
    /// data already in the database survives the migrations run to get there.
    /// </summary>
    [Theory]
    [MemberData(nameof(RevisionsBeforeHead))]
    public void A_database_at_an_earlier_revision_upgrades_to_match_a_fresh_install(int revisionNumber)
    {
        using var temp = new TempDirectory();
        var path = temp.Join($"revision-{revisionNumber}.sqlite3");
        BuildDatabaseAtRevision(path, revisionNumber);

        // Representative rows in tables that exist from the baseline onward and are never rebuilt, so this
        // seeding works at every revision without knowing which migrations are ahead of it.
        SchemaSnapshot.Execute(
            path,
            "UPDATE suite_settings SET product_display_name = 'Generic test install';" +
            "INSERT INTO activity_events (id, event_type, module, title) VALUES (999999, 'generic_test.seed', 'test', 'Generic test event');");

        var database = new SqliteDatabase(path);
        var outcome = new SchemaMigrator(database).EnsureAtHead();
        database.ClearPool();
        Assert.Equal(SchemaStartupOutcome.Upgraded, outcome);

        using var freshTemp = new TempDirectory();
        var freshPath = freshTemp.Join("fresh.sqlite3");
        var freshDatabase = new SqliteDatabase(freshPath);
        Assert.Equal(SchemaStartupOutcome.Created, new SchemaMigrator(freshDatabase).EnsureAtHead());
        freshDatabase.ClearPool();

        // Schema only: the fresh install and the seeded rows above intentionally hold different rows.
        Assert.Equal(SchemaSnapshot.DescribeSchema(freshPath), SchemaSnapshot.DescribeSchema(path));
        Assert.Equal(1, SchemaSnapshot.ScalarLong(path, "SELECT COUNT(*) FROM suite_settings WHERE product_display_name = 'Generic test install'"));
        Assert.Equal(1, SchemaSnapshot.ScalarLong(path, "SELECT COUNT(*) FROM activity_events WHERE id = 999999 AND title = 'Generic test event'"));
    }

    /// <summary>Applies migrations 1..<paramref name="revisionNumber"/> and records that revision, the same as <c>SchemaMigrator</c> does once its scripts have run.</summary>
    private static void BuildDatabaseAtRevision(string path, int revisionNumber)
    {
        var migrations = SchemaMigrator.Migrations.Where(migration => migration.Number <= revisionNumber).OrderBy(migration => migration.Number).ToList();
        SchemaSnapshot.Execute(path, string.Concat(migrations.Select(SchemaMigrator.ReadMigrationSql)));
        var renamed = SchemaSnapshot.ScalarLong(path, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'schema_version'") > 0;
        var (table, column) = renamed ? ("schema_version", "revision") : ("alembic_version", "version_num");
        SchemaSnapshot.Execute(path, $"DELETE FROM {table}; INSERT INTO {table} ({column}) VALUES ('{migrations[^1].Revision}');");
    }

    [Fact]
    public void An_empty_version_table_is_unversioned_and_several_rows_are_incompatible()
    {
        using var temp = new TempDirectory();
        var empty = temp.Join("empty.sqlite3");
        SchemaSnapshot.Execute(empty, "CREATE TABLE alembic_version (version_num VARCHAR(32) NOT NULL);");
        var emptyDatabase = new SqliteDatabase(empty);
        Assert.Equal(
            SchemaMismatchKind.Unversioned,
            Assert.Throws<DatabaseSchemaMismatchException>(() => new SchemaMigrator(emptyDatabase).EnsureAtHead()).Kind);
        emptyDatabase.ClearPool();

        var several = temp.Join("several.sqlite3");
        SchemaSnapshot.Execute(several, "CREATE TABLE alembic_version (version_num VARCHAR(32) NOT NULL); INSERT INTO alembic_version VALUES ('a'), ('b');");
        var severalDatabase = new SqliteDatabase(several);
        var error = Assert.Throws<DatabaseSchemaMismatchException>(() => new SchemaMigrator(severalDatabase).EnsureAtHead());
        severalDatabase.ClearPool();
        Assert.Equal(SchemaMismatchKind.Incompatible, error.Kind);
        Assert.Contains("('a', 'b')", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Connections_apply_wal_foreign_keys_busy_timeout_and_synchronous_pragmas()
    {
        using var temp = new TempDirectory();
        var database = new SqliteDatabase(temp.Join("weir.sqlite3"));
        using (var connection = database.Open())
        {
            Assert.Equal("wal", Pragma(connection, "journal_mode"));
            Assert.Equal("1", Pragma(connection, "foreign_keys"));
            Assert.Equal("30000", Pragma(connection, "busy_timeout"));
            Assert.Equal("1", Pragma(connection, "synchronous"));
        }

        database.ClearPool();
    }

    [Fact]
    public async Task The_health_probe_restores_the_busy_timeout()
    {
        using var temp = new TempDirectory();
        var database = new SqliteDatabase(temp.Join("weir.sqlite3"));
        using (database.Open())
        {
        }

        Assert.True(await database.IsConnectedAsync());
        using (var connection = database.Open())
        {
            Assert.Equal("30000", Pragma(connection, "busy_timeout"));
        }

        database.ClearPool();
    }

    [Fact]
    public async Task The_health_probe_reports_an_unopenable_database_as_disconnected()
    {
        using var temp = new TempDirectory();
        var directoryInsteadOfFile = temp.Join("a-directory");
        Directory.CreateDirectory(directoryInsteadOfFile);
        Assert.False(await new SqliteDatabase(directoryInsteadOfFile).IsConnectedAsync());
    }

    [Fact]
    public async Task The_health_probe_reports_a_database_file_that_has_gone_as_disconnected()
    {
        using var temp = new TempDirectory();
        var path = temp.Join("weir.sqlite3");
        var database = new SqliteDatabase(path);
        using (database.Open())
        {
        }

        Assert.True(await database.IsConnectedAsync());
        database.ClearPool();
        File.Delete(path);

        Assert.False(await database.IsConnectedAsync());
        database.ClearPool();
    }

    [Fact]
    public async Task Log_retention_days_come_from_suite_settings()
    {
        using var temp = new TempDirectory();
        var database = new SqliteDatabase(temp.Join("weir.sqlite3"));
        new SchemaMigrator(database).EnsureAtHead();
        using var logFile = new WeirLogFile(temp.Join("weir.log"), TimeProvider.System);
        var retention = new LogRetentionTask(database, logFile, TimeProvider.System, new SuiteSettingsStore(new AuthStore()));
        Assert.Equal(30, await retention.ReadKeepDaysAsync(database));

        using (var connection = database.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE suite_settings SET log_retention_days = 0";
            command.ExecuteNonQuery();
        }

        Assert.Equal(1, await retention.ReadKeepDaysAsync(database));

        using (var connection = database.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM suite_settings";
            command.ExecuteNonQuery();
        }

        // ensure_suite_settings_row: a missing row is created with its defaults.
        Assert.Equal(30, await retention.ReadKeepDaysAsync(database));
        using (var connection = database.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT count(*) FROM suite_settings";
            Assert.Equal(1L, command.ExecuteScalar());
        }

        database.ClearPool();
    }

    /// <summary>A baseline database with its recorded revision replaced, for tests of the refusal path that only care what revision is stored, not the real schema at that revision.</summary>
    private static string DatabaseStampedAtRevision(TempDirectory temp, string revision)
    {
        var path = temp.Join("stamped.sqlite3");
        var database = new SqliteDatabase(path);
        new SchemaMigrator(database).EnsureAtBaseline();
        database.ClearPool();
        SchemaSnapshot.Execute(path, $"UPDATE alembic_version SET version_num = '{revision}';");
        return path;
    }

    private static string Pragma(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name}";
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
    }
}
