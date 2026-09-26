using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// Migration <c>0026_user_app_theme.sql</c> (#790): every existing user gets no theme preference, so an
/// upgrade leaves them following the system setting until they choose one.
/// </summary>
public sealed class UserAppThemeMigrationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public UserAppThemeMigrationTests()
    {
        _database = new SqliteDatabase(_temp.Join("weir.sqlite3"));
        new SchemaMigrator(_database).EnsureAtHead();
    }

    public void Dispose()
    {
        _database.ClearPool();
        _temp.Dispose();
    }

    [Fact]
    public void An_existing_user_has_no_theme_preference_until_one_is_chosen()
    {
        Execute("INSERT INTO users (username, password_hash, role, is_active) VALUES ('alice', 'x', 'admin', 1)");

        Assert.Equal(DBNull.Value, Scalar("SELECT app_theme FROM users WHERE username = 'alice'"));
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
