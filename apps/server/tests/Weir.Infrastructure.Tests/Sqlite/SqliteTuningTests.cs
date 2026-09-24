using System.Globalization;
using Microsoft.Data.Sqlite;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite;

/// <summary>The cache and journal settings every connection gets (#711).</summary>
public sealed class SqliteTuningTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public SqliteTuningTests()
    {
        _database = new SqliteDatabase(_temp.Join("weir.sqlite3"));
        new SchemaMigrator(_database).EnsureAtHead();
    }

    public void Dispose()
    {
        _database.ClearPool();
        _temp.Dispose();
    }

    [Theory]
    [InlineData("cache_size", -8000L)]
    [InlineData("temp_store", 2L)]
    [InlineData("mmap_size", 268435456L)]
    [InlineData("journal_size_limit", 67108864L)]
    public void A_connection_is_opened_with_the_tuned_setting(string pragma, long expected)
    {
        using var connection = _database.Open();

        Assert.Equal(expected, Scalar(connection, $"PRAGMA {pragma}"));
    }

    [Fact]
    public async Task The_settings_are_applied_once_per_native_connection_not_on_every_open()
    {
        var first = await _database.OpenAsync();
        var handle = first.Handle;
        Execute(first, "PRAGMA cache_size=-100");
        await first.DisposeAsync();

        await using var second = await _database.OpenAsync();

        Assert.Same(handle, second.Handle);
        Assert.Equal(-100L, Scalar(second, "PRAGMA cache_size"));
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
