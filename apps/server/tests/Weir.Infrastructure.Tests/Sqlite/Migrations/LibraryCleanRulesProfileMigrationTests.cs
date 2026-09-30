using System.Globalization;
using Microsoft.Data.Sqlite;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// Migration <c>0030_library_clean_rules_profile.sql</c>: a library's own cleaning profile starts out as "the same as the
/// workflow", and a profile a library points at cannot be deleted from under it.
/// </summary>
public sealed class LibraryCleanRulesProfileMigrationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public LibraryCleanRulesProfileMigrationTests()
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
    public void A_library_has_no_profile_of_its_own_until_one_is_chosen()
    {
        Execute("INSERT INTO libraries (name, media_type, watched_folder, display_order) VALUES ('Movies', 'movie', '/in', 1)");

        Assert.Equal(DBNull.Value, Scalar("SELECT library_rule_set_id FROM libraries WHERE name = 'Movies'"));
    }

    [Fact]
    public void A_profile_a_library_cleans_by_cannot_be_deleted()
    {
        Execute("INSERT INTO rule_sets (name) VALUES ('Cleaning')");
        var profileId = Convert.ToInt64(Scalar("SELECT id FROM rule_sets WHERE name = 'Cleaning'"), CultureInfo.InvariantCulture);
        Execute($"INSERT INTO libraries (name, media_type, watched_folder, display_order, library_rule_set_id) VALUES ('Movies', 'movie', '/in', 1, {profileId})");

        Assert.Throws<SqliteException>(() => Execute($"DELETE FROM rule_sets WHERE id = {profileId}"));
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
}
