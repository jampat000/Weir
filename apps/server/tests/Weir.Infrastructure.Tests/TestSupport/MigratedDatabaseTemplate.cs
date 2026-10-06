using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests;

/// <summary>
/// A database with every migration applied, built once per test process and copied as a plain file into each
/// test's own folder. Creating and migrating a database from nothing takes about a tenth of a second and the
/// result is the same every time; the copy takes a few milliseconds. Nothing is shared once a test has its copy,
/// so one test cannot see or change another's data.
/// </summary>
/// <remarks>
/// The template lives only as long as the test process, so it is rebuilt from the migrations in the assembly
/// under test on every run and no cache key can go stale. A test that opens its copy still goes through
/// <see cref="SchemaMigrator.EnsureAtHead"/>, which refuses a database whose recorded revision is not head.
/// Tests of the migrations themselves, and of first start-up on an empty folder, do not use it.
/// </remarks>
internal static class MigratedDatabaseTemplate
{
    private static readonly Lazy<string> Template = new(Build);

    static MigratedDatabaseTemplate()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteQuietly();
    }

    /// <summary>Puts a migrated database at <paramref name="databasePath"/> and creates the folder it lives in.</summary>
    public static void CopyTo(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        File.Copy(Template.Value, databasePath, overwrite: false);
    }

    private static string Build()
    {
        var folder = Path.Join(Path.GetTempPath(), "weir-test-template-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Join(folder, "weir.sqlite3");
        var database = new SqliteDatabase(path);
        new SchemaMigrator(database).EnsureAtHead();

        // Fold the write-ahead log into the database so the copy is one file, then let go of the pooled connections.
        using (var connection = database.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
        }

        database.ClearPool();
        File.Delete(path + "-wal");
        File.Delete(path + "-shm");
        return path;
    }

    private static void DeleteQuietly()
    {
        try
        {
            if (Template.IsValueCreated)
            {
                Directory.Delete(Path.GetDirectoryName(Template.Value)!, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left in the temp folder; nothing depends on it.
        }
    }
}
