using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite;

/// <summary>
/// The schema revision lives in <c>schema_version</c>. A database from before migration 39 records it in
/// <c>alembic_version</c> and is carried over on its first start; a database holding both tables is refused.
/// </summary>
public sealed class SchemaVersionTableTests
{
    private const int RenameMigration = 39;

    [Fact]
    public void A_new_database_records_its_revision_in_schema_version_only()
    {
        using var temp = new TempDirectory();
        var path = temp.Join("weir.sqlite3");

        var database = new SqliteDatabase(path);
        Assert.Equal(SchemaStartupOutcome.Created, new SchemaMigrator(database).EnsureAtHead());
        database.ClearPool();

        AssertOnlySchemaVersionAtHead(path);
    }

    [Fact]
    public void A_database_at_the_baseline_ends_with_schema_version_only()
    {
        using var temp = new TempDirectory();
        var path = temp.Join("weir.sqlite3");
        var baseline = new SqliteDatabase(path);
        new SchemaMigrator(baseline).EnsureAtBaseline();
        baseline.ClearPool();
        Assert.Equal(1, SchemaSnapshot.ScalarLong(path, "SELECT COUNT(*) FROM alembic_version"));

        var database = new SqliteDatabase(path);
        Assert.Equal(SchemaStartupOutcome.Upgraded, new SchemaMigrator(database).EnsureAtHead());
        database.ClearPool();

        AssertOnlySchemaVersionAtHead(path);
    }

    [Fact]
    public void A_database_recorded_in_the_old_table_is_carried_over_on_its_first_start()
    {
        using var temp = new TempDirectory();
        var path = temp.Join("weir.sqlite3");
        var previous = SchemaMigrator.Migrations.Single(m => m.Number == RenameMigration - 1).Revision;
        SchemaSnapshot.Execute(
            path,
            MigrationsBefore(RenameMigration) + $"DELETE FROM alembic_version; INSERT INTO alembic_version (version_num) VALUES ('{previous}');");

        var database = new SqliteDatabase(path);
        Assert.Equal(SchemaStartupOutcome.Upgraded, new SchemaMigrator(database).EnsureAtHead());
        database.ClearPool();

        AssertOnlySchemaVersionAtHead(path);

        var again = new SqliteDatabase(path);
        Assert.Equal(SchemaStartupOutcome.AlreadyCurrent, new SchemaMigrator(again).EnsureAtHead());
        again.ClearPool();
    }

    [Fact]
    public void A_database_holding_both_version_tables_is_refused_and_left_unchanged()
    {
        using var temp = new TempDirectory();
        var path = temp.Join("weir.sqlite3");
        var created = new SqliteDatabase(path);
        new SchemaMigrator(created).EnsureAtHead();
        created.ClearPool();
        SchemaSnapshot.Execute(
            path,
            $"CREATE TABLE alembic_version (version_num VARCHAR(32) NOT NULL); INSERT INTO alembic_version VALUES ('{SchemaMigrator.HeadRevision}');");
        var before = SchemaSnapshot.Describe(path);

        var database = new SqliteDatabase(path);
        var error = Assert.Throws<DatabaseSchemaMismatchException>(() => new SchemaMigrator(database).EnsureAtHead());
        database.ClearPool();

        Assert.Equal(SchemaMismatchKind.Incompatible, error.Kind);
        Assert.Contains("'schema_version' and an 'alembic_version' table", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, SchemaSnapshot.Describe(path));
    }

    [Fact]
    public void An_empty_schema_version_table_is_unversioned_and_several_rows_are_incompatible()
    {
        using var temp = new TempDirectory();
        var empty = temp.Join("empty.sqlite3");
        SchemaSnapshot.Execute(empty, "CREATE TABLE schema_version (revision VARCHAR(32) NOT NULL);");
        var emptyDatabase = new SqliteDatabase(empty);
        Assert.Equal(
            SchemaMismatchKind.Unversioned,
            Assert.Throws<DatabaseSchemaMismatchException>(() => new SchemaMigrator(emptyDatabase).EnsureAtHead()).Kind);
        emptyDatabase.ClearPool();

        var several = temp.Join("several.sqlite3");
        SchemaSnapshot.Execute(several, "CREATE TABLE schema_version (revision VARCHAR(32) NOT NULL); INSERT INTO schema_version VALUES ('a'), ('b');");
        var severalDatabase = new SqliteDatabase(several);
        var error = Assert.Throws<DatabaseSchemaMismatchException>(() => new SchemaMigrator(severalDatabase).EnsureAtHead());
        severalDatabase.ClearPool();
        Assert.Equal(SchemaMismatchKind.Incompatible, error.Kind);
        Assert.Contains("('a', 'b')", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_revision_in_schema_version_is_refused_untouched()
    {
        using var temp = new TempDirectory();
        var path = temp.Join("weir.sqlite3");
        var created = new SqliteDatabase(path);
        new SchemaMigrator(created).EnsureAtHead();
        created.ClearPool();
        SchemaSnapshot.Execute(path, "UPDATE schema_version SET revision = '0099_from_the_future';");
        var before = SchemaSnapshot.Describe(path);

        var database = new SqliteDatabase(path);
        var error = Assert.Throws<DatabaseSchemaMismatchException>(() => new SchemaMigrator(database).EnsureAtHead());
        database.ClearPool();

        Assert.Equal(SchemaMismatchKind.UnknownRevision, error.Kind);
        Assert.StartsWith("Database revision '0099_from_the_future' is not recognized by this Weir build", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, SchemaSnapshot.Describe(path));
    }

    /// <summary>The scripts of every migration before <paramref name="number"/>: a database as the release before it left it, with the baseline's version table.</summary>
    private static string MigrationsBefore(int number) =>
        string.Concat(SchemaMigrator.Migrations.Where(m => m.Number < number).OrderBy(m => m.Number).Select(SchemaMigrator.ReadMigrationSql));

    private static void AssertOnlySchemaVersionAtHead(string path)
    {
        Assert.Equal(0, SchemaSnapshot.ScalarLong(path, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'alembic_version'"));
        Assert.Equal(1, SchemaSnapshot.ScalarLong(path, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_version'"));
        Assert.Equal(1, SchemaSnapshot.ScalarLong(path, "SELECT COUNT(*) FROM schema_version"));
        Assert.Equal(1, SchemaSnapshot.ScalarLong(path, $"SELECT COUNT(*) FROM schema_version WHERE revision = '{SchemaMigrator.HeadRevision}'"));
    }
}
