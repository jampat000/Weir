using Microsoft.Data.Sqlite;
using Weir.Core.Json;
using Weir.Core.Time;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>One <c>download_client_connections</c> row.</summary>
public sealed record DownloadClientConnectionRecord(
    long Id,
    string Kind,
    string Name,
    bool Enabled,
    string BaseUrl,
    string? Username,
    string? PasswordCiphertext,
    string? ApiKeyCiphertext,
    bool? LastTestOk,
    Timestamp? LastTestAt,
    string? LastTestDetail)
{
    /// <summary>The connection as the API returns it: secrets are reported only as saved or not.</summary>
    public WireObject ToOut() => new WireObject()
        .Set("id", Id)
        .Set("kind", Kind)
        .Set("name", Name)
        .Set("enabled", Enabled)
        .Set("base_url", BaseUrl)
        .Set("username", Username)
        .Set("password_is_saved", !string.IsNullOrEmpty(PasswordCiphertext))
        .Set("api_key_is_saved", !string.IsNullOrEmpty(ApiKeyCiphertext))
        .Set("last_test_ok", LastTestOk is { } ok ? WireValue.Of(ok) : WireValue.Null)
        .Set("last_test_at", LastTestAt is { } at ? at.ToWireText() : null)
        .Set("last_test_detail", LastTestDetail);
}

/// <summary>
/// Explicit SQL over <c>download_client_connections</c>. No lanes, no webhook secret — Weir only ever reads
/// from these. A stateless singleton (#745 part 5): every call still takes the caller's own <see cref="UnitOfWork"/>.
/// </summary>
public sealed class DownloadClientConnectionStore
{
    private const string Columns =
        "id, kind, name, enabled, base_url, username, password_ciphertext, api_key_ciphertext, " +
        "last_connection_test_ok, last_connection_test_at, last_connection_test_detail";

    public Task<List<DownloadClientConnectionRecord>> ListAsync(UnitOfWork uow)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.QueryAsync($"SELECT {Columns} FROM download_client_connections ORDER BY id", ReadRow);
    }

    public Task<List<DownloadClientConnectionRecord>> ListEnabledAsync(UnitOfWork uow)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.QueryAsync($"SELECT {Columns} FROM download_client_connections WHERE enabled IS 1 ORDER BY id", ReadRow);
    }

    public Task<DownloadClientConnectionRecord?> GetAsync(UnitOfWork uow, long connectionId)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.QuerySingleAsync($"SELECT {Columns} FROM download_client_connections WHERE id = $id", ReadRow, ("$id", connectionId));
    }

    public async Task<bool> NameExistsAsync(UnitOfWork uow, string name, long? exceptId = null)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var id = await uow.ScalarAsync("SELECT id FROM download_client_connections WHERE name = $name LIMIT 1", ("$name", name)).ConfigureAwait(false);
        return id is not null and not DBNull && (exceptId is null || Convert.ToInt64(id, System.Globalization.CultureInfo.InvariantCulture) != exceptId);
    }

    /// <summary>Insert a connection; returns the new id.</summary>
    public async Task<long> InsertAsync(
        UnitOfWork uow, string kind, string name, bool enabled, string baseUrl, string? username, string? passwordCiphertext, string? apiKeyCiphertext)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var id = await uow.ExecuteScalarWriteAsync(
            "INSERT INTO download_client_connections (kind, name, enabled, base_url, username, password_ciphertext, api_key_ciphertext, " +
            "last_connection_test_ok, last_connection_test_at, last_connection_test_detail) " +
            "VALUES ($kind, $name, $enabled, $base_url, $username, $password, $key, NULL, NULL, NULL) RETURNING id",
            ("$kind", kind),
            ("$name", name),
            ("$enabled", enabled ? 1 : 0),
            ("$base_url", baseUrl),
            ("$username", username),
            ("$password", passwordCiphertext),
            ("$key", apiKeyCiphertext)).ConfigureAwait(false);
        return Convert.ToInt64(id, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Write the changed columns and stamp <c>updated_at</c>. Nothing changed, nothing written.</summary>
    public async Task UpdateColumnsAsync(UnitOfWork uow, long connectionId, IReadOnlyList<(string Column, object? Value)> changes)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0)
        {
            return;
        }

        var sets = changes.Select((change, index) => $"{change.Column} = $v{index}");
        var parameters = changes.Select((change, index) => ($"$v{index}", change.Value)).Append(("$id", (object?)connectionId)).ToArray();
        await uow.ExecuteAsync(
            $"UPDATE download_client_connections SET {string.Join(", ", sets)}, updated_at = CURRENT_TIMESTAMP WHERE id = $id",
            parameters).ConfigureAwait(false);
    }

    public Task DeleteAsync(UnitOfWork uow, long connectionId)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.ExecuteAsync("DELETE FROM download_client_connections WHERE id = $id", ("$id", connectionId));
    }

    /// <summary>The conditional test-result write: 0 when the connection was removed meanwhile.</summary>
    public Task<int> RecordTestResultAsync(UnitOfWork uow, long connectionId, bool ok, Timestamp checkedAt, string detail)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.ExecuteAsync(
            "UPDATE download_client_connections SET last_connection_test_ok = $ok, last_connection_test_at = $at, " +
            "last_connection_test_detail = $detail, updated_at = CURRENT_TIMESTAMP WHERE id = $id",
            ("$ok", ok ? 1 : 0),
            ("$at", checkedAt.ToSqlite()),
            ("$detail", detail),
            ("$id", connectionId));
    }

    private static DownloadClientConnectionRecord ReadRow(SqliteDataReader reader) => new(
        SqliteValues.GetInt64(reader, 0),
        SqliteValues.GetString(reader, 1),
        SqliteValues.GetString(reader, 2),
        SqliteValues.GetBool(reader, 3),
        SqliteValues.GetString(reader, 4),
        SqliteValues.GetStringOrNull(reader, 5),
        SqliteValues.GetStringOrNull(reader, 6),
        SqliteValues.GetStringOrNull(reader, 7),
        SqliteValues.GetBoolOrNull(reader, 8),
        SqliteValues.GetDateTimeOrNull(reader, 9),
        SqliteValues.GetStringOrNull(reader, 10));
}
