using Microsoft.Data.Sqlite;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Time;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.ConnectionTraffic;
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
    string? LastTestDetail,
    string? Nickname = null,
    long? LastAnswerMs = null,
    Timestamp? LastUsedAt = null)
{
    /// <summary>How this connection is written in a sentence, led by its product and followed by its nickname.</summary>
    public string Label => DownloadClientKinds.LabelForConnection(Kind, Name, Nickname);

    /// <summary>The connection as the API returns it: secrets are reported only as saved or not.</summary>
    public WireObject ToOut() => new WireObject()
        .Set("id", Id)
        .Set("kind", Kind)
        .Set("name", Name)
        .Set("nickname", Nickname)
        .Set("enabled", Enabled)
        .Set("base_url", BaseUrl)
        .Set("username", Username)
        .Set("password_is_saved", !string.IsNullOrEmpty(PasswordCiphertext))
        .Set("api_key_is_saved", !string.IsNullOrEmpty(ApiKeyCiphertext))
        .Set("last_test_ok", LastTestOk is { } ok ? WireValue.Of(ok) : WireValue.Null)
        .Set("last_test_at", LastTestAt is { } at ? at.ToWireText() : null)
        .Set("last_test_detail", LastTestDetail)
        .Set("last_answer_ms", LastAnswerMs)
        .Set("last_used_at", ConnectionUsage.WireText(LastUsedAt));
}

/// <summary>
/// Explicit SQL over <c>download_client_connections</c>. No lanes, no webhook secret — Weir only ever reads
/// from these. A stateless singleton (#745 part 5): every call still takes the caller's own <see cref="UnitOfWork"/>.
/// </summary>
public sealed class DownloadClientConnectionStore
{
    private const string Columns =
        "id, kind, name, enabled, base_url, username, password_ciphertext, api_key_ciphertext, " +
        "last_connection_test_ok, last_connection_test_at, last_connection_test_detail, nickname, last_answer_ms, last_used_at";

    private readonly ConnectionUsageLedger? _usage;
    private readonly DataChangePublisher? _changes;

    /// <param name="usage">Newer usage than the database holds, laid over every connection a list or lookup returns; none reads the database alone.</param>
    /// <param name="changes">Told, once a write commits, that the connections changed, so every open screen reads them again.</param>
    public DownloadClientConnectionStore(ConnectionUsageLedger? usage = null, DataChangePublisher? changes = null)
    {
        _usage = usage;
        _changes = changes;
    }

    public async Task<List<DownloadClientConnectionRecord>> ListAsync(UnitOfWork uow)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var rows = await uow.QueryAsync($"SELECT {Columns} FROM download_client_connections ORDER BY id", ReadRow).ConfigureAwait(false);
        return [.. rows.Select(WithLiveUsage)];
    }

    public Task<List<DownloadClientConnectionRecord>> ListEnabledAsync(UnitOfWork uow)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.QueryAsync($"SELECT {Columns} FROM download_client_connections WHERE enabled IS 1 ORDER BY id", ReadRow);
    }

    public async Task<DownloadClientConnectionRecord?> GetAsync(UnitOfWork uow, long connectionId)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var row = await uow.QuerySingleAsync($"SELECT {Columns} FROM download_client_connections WHERE id = $id", ReadRow, ("$id", connectionId)).ConfigureAwait(false);
        return row is null ? null : WithLiveUsage(row);
    }

    /// <summary>
    /// Give every connection the name derived from its kind and address (<see cref="ConnectionNaming"/>). Every write
    /// that can change a derived name does this itself; startup calls it to bring names stored before they were derived in line.
    /// </summary>
    public Task RefreshNamesAsync(UnitOfWork uow) =>
        ConnectionNameColumn.RefreshAsync(uow, "download_client_connections", DownloadClientKinds.ProductLabel);

    /// <summary>Insert a connection; returns the new id.</summary>
    public async Task<long> InsertAsync(
        UnitOfWork uow, string kind, bool enabled, string baseUrl, string? username, string? passwordCiphertext, string? apiKeyCiphertext, string? nickname = null)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var id = await uow.ExecuteScalarWriteAsync(
            "INSERT INTO download_client_connections (kind, name, enabled, base_url, username, password_ciphertext, api_key_ciphertext, " +
            "last_connection_test_ok, last_connection_test_at, last_connection_test_detail, nickname) " +
            "VALUES ($kind, $name, $enabled, $base_url, $username, $password, $key, NULL, NULL, NULL, $nickname) RETURNING id",
            ("$kind", kind),
            ("$name", ConnectionNameColumn.NewPlaceholder()),
            ("$enabled", enabled ? 1 : 0),
            ("$base_url", baseUrl),
            ("$username", username),
            ("$password", passwordCiphertext),
            ("$key", apiKeyCiphertext),
            ("$nickname", nickname)).ConfigureAwait(false);
        await RefreshNamesAsync(uow).ConfigureAwait(false);
        Changed(uow);
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
        if (changes.Any(change => change.Column == "base_url"))
        {
            await RefreshNamesAsync(uow).ConfigureAwait(false);
        }

        Changed(uow);
    }

    public async Task DeleteAsync(UnitOfWork uow, long connectionId)
    {
        ArgumentNullException.ThrowIfNull(uow);
        await uow.ExecuteAsync("DELETE FROM download_client_connections WHERE id = $id", ("$id", connectionId)).ConfigureAwait(false);
        await RefreshNamesAsync(uow).ConfigureAwait(false);
        _usage?.Forget(new ConnectionRef(ConnectionKind.DownloadClient, connectionId));
        Changed(uow);
    }

    /// <summary>Save how long the last call took and when the connection was last used; a value the usage does not carry stays as it was.</summary>
    public Task<int> RecordUsageAsync(UnitOfWork uow, long connectionId, ConnectionUsage usage)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.ExecuteAsync(
            "UPDATE download_client_connections SET last_answer_ms = COALESCE($ms, last_answer_ms), last_used_at = COALESCE($at, last_used_at) WHERE id = $id",
            ("$ms", usage.AnswerMilliseconds),
            ("$at", usage.UsedAt?.ToSqlite()),
            ("$id", connectionId));
    }

    private DownloadClientConnectionRecord WithLiveUsage(DownloadClientConnectionRecord row)
    {
        if (_usage is null)
        {
            return row;
        }

        var usage = _usage.Overlay(new ConnectionRef(ConnectionKind.DownloadClient, row.Id), row.LastAnswerMs, row.LastUsedAt);
        return row with { LastAnswerMs = usage.AnswerMilliseconds, LastUsedAt = usage.UsedAt };
    }

    /// <summary>The conditional test-result write: 0 when the connection was removed meanwhile.</summary>
    public async Task<int> RecordTestResultAsync(UnitOfWork uow, long connectionId, bool ok, Timestamp checkedAt, string detail)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var written = await uow.ExecuteAsync(
            "UPDATE download_client_connections SET last_connection_test_ok = $ok, last_connection_test_at = $at, " +
            "last_connection_test_detail = $detail, updated_at = CURRENT_TIMESTAMP WHERE id = $id",
            ("$ok", ok ? 1 : 0),
            ("$at", checkedAt.ToSqlite()),
            ("$detail", detail),
            ("$id", connectionId)).ConfigureAwait(false);
        if (written > 0)
        {
            Changed(uow);
        }

        return written;
    }

    private void Changed(UnitOfWork uow) => _changes?.PublishOnCommit(uow, DataTopics.Connections);

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
        SqliteValues.GetStringOrNull(reader, 10),
        SqliteValues.GetStringOrNull(reader, 11),
        SqliteValues.GetInt64OrNull(reader, 12),
        SqliteValues.GetDateTimeOrNull(reader, 13));
}
