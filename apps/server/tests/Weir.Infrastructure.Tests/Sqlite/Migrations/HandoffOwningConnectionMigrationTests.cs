using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// <c>0016_handoff_owning_connection.sql</c>: backfilling which connection an existing hand-off belongs to. The
/// <c>ALTER TABLE</c> that adds the column cannot be replayed against an already-migrated database (SQLite refuses
/// a column that already exists), so this proves the backfill query itself — the same one the migration runs once,
/// at upgrade time — against rows inserted after that column already exists.
/// </summary>
public sealed class HandoffOwningConnectionMigrationTests : IDisposable
{
    private const string BackfillQuery =
        """
        UPDATE media_manager_handoffs
        SET connection_id = (
            SELECT c.id FROM media_manager_connections c
            WHERE c.kind = media_manager_handoffs.source_key AND c.enabled IS 1
        )
        WHERE (
            SELECT count(*) FROM media_manager_connections c
            WHERE c.kind = media_manager_handoffs.source_key AND c.enabled IS 1
        ) = 1
        """;

    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public HandoffOwningConnectionMigrationTests()
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
    public void A_hand_off_of_a_kind_with_exactly_one_enabled_connection_is_backfilled_to_it()
    {
        Execute("INSERT INTO media_manager_connections (id, kind, name, enabled, base_url) VALUES (1, 'deluno', 'Main', 1, 'http://manager.local')");
        Execute("INSERT INTO media_manager_handoffs (source_key, handoff_id, relative_path) VALUES ('deluno', 'h1', 'Film/film.mkv')");

        Execute(BackfillQuery);

        Assert.Equal(1L, ScalarOrNull("SELECT connection_id FROM media_manager_handoffs WHERE handoff_id = 'h1'"));
    }

    [Fact]
    public void A_hand_off_of_a_kind_with_several_or_no_enabled_connections_is_left_unowned()
    {
        Execute("INSERT INTO media_manager_connections (id, kind, name, enabled, base_url) VALUES (2, 'radarr', '1080p', 1, 'http://one.local')");
        Execute("INSERT INTO media_manager_connections (id, kind, name, enabled, base_url) VALUES (3, 'radarr', '4K', 1, 'http://two.local')");
        Execute("INSERT INTO media_manager_handoffs (source_key, handoff_id, relative_path) VALUES ('radarr', 'ambiguous', 'a.mkv')");
        Execute("INSERT INTO media_manager_handoffs (source_key, handoff_id, relative_path) VALUES ('native', 'connectionless', 'b.mkv')");

        Execute(BackfillQuery);

        Assert.Null(ScalarOrNull("SELECT connection_id FROM media_manager_handoffs WHERE handoff_id = 'ambiguous'"));
        Assert.Null(ScalarOrNull("SELECT connection_id FROM media_manager_handoffs WHERE handoff_id = 'connectionless'"));
    }

    private void Execute(string sql)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long? ScalarOrNull(string sql)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
