using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// Migration <c>0026_user_app_theme.sql</c>: every existing user gets no theme preference. The client
/// adopts a browser's existing choice onto the account afterwards; this migration only proves the
/// column starts empty.
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
