using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.MediaManagers;

/// <summary>What the Processing file row (<c>files</c>) holds for a hand-off's file when a test seeds one.</summary>
internal sealed record FileColumns(string? NextRetryAt = null, string? StatusReason = null, int? FailureAttempts = null);

/// <summary>Steps on hand-offs, as Deluno takes them: the webhook that hands a file over, the status poll and the cancel.</summary>
internal static class Handoffs
{
    public const string SecretValue = "s3cret";

    /// <summary>Hand-off answers are kept this long once finished (the server's ledger retention).</summary>
    public const int LedgerRetentionDays = 90;

    public static readonly IReadOnlyDictionary<string, string> Secret = new Dictionary<string, string> { ["X-Webhook-Secret"] = SecretValue };

    public static IReadOnlyDictionary<string, string> SecretEnvironment { get; } = ManagerEnvironment.WebhookSecret(SecretValue);

    public static string WebhookPath { get; } = $"{WeirClient.Api}/intake/webhook/deluno";

    public static string NewId() => $"h-{Guid.NewGuid().ToString("N")[..12]}";

    public static string StatusPath(string handoffId) => $"{WeirClient.Api}/intake/handoffs/deluno/{handoffId}";

    public static JsonObject Body(string handoffId, string sourcePath) => new()
    {
        ["eventType"] = "deluno.processor-handoff",
        ["handoffId"] = handoffId,
        ["libraryId"] = "lib-1",
        ["mediaType"] = "movies",
        ["sourcePath"] = sourcePath,
        ["callbackPath"] = RemuxJobs.CallbackPath,
    };

    // A server restarts on a new port whenever a test seeds its database, so each call reaches the server by its current address.
    public static async Task<WeirResponse> HandOffResponseAsync(WeirServer server, string handoffId, string sourcePath)
    {
        using var manager = server.CreateClient();
        return await manager.PostAsync(WebhookPath, Body(handoffId, sourcePath), Secret);
    }

    public static async Task HandOffAsync(WeirServer server, string handoffId, string sourcePath)
    {
        var response = await HandOffResponseAsync(server, handoffId, sourcePath);
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
    }

    /// <summary>The hand-off's status; <paramref name="headers"/> default to the right secret.</summary>
    public static async Task<WeirResponse> StatusResponseAsync(
        WeirServer server, string handoffId, IReadOnlyDictionary<string, string>? headers = null)
    {
        using var manager = server.CreateClient();
        return await manager.GetAsync(StatusPath(handoffId), headers ?? Secret);
    }

    public static async Task<JsonObject> StatusAsync(WeirServer server, string handoffId)
    {
        var response = await StatusResponseAsync(server, handoffId);
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        return response.Fields;
    }

    public static async Task<WeirResponse> CancelAsync(WeirServer server, string handoffId)
    {
        using var manager = server.CreateClient();
        return await manager.DeleteAsync(StatusPath(handoffId), Secret);
    }

    /// <summary>
    /// While the server is stopped: moves the hand-off's job rows, and gives its file a Files row. A <c>leased</c> row cannot be seeded
    /// this way, because startup treats it as a dead worker's and requeues it; the tests that need running work use a real worker.
    /// </summary>
    public static async Task SeedAsync(
        WeirServer server,
        string handoffId,
        string? jobStatus = null,
        string? fileStatus = null,
        string? relativePath = null,
        FileColumns? file = null)
    {
        await using var database = await server.StopForDatabaseAsync();
        var connection = database.Connection;
        if (jobStatus is not null)
        {
            var key = RemuxJobs.HandoffDedupeKey(handoffId);
            var changed = SeedSql.Execute(
                connection,
                "UPDATE jobs SET status = $status WHERE dedupe_key = $key OR dedupe_key LIKE $children",
                ("$status", jobStatus), ("$key", key), ("$children", $"{key}:%"));
            Assert.True(changed > 0, $"no job rows for {handoffId}");
        }

        if (fileStatus is null)
        {
            return;
        }

        var ledger = SeedSql.Rows(
            connection,
            "SELECT library_id, relative_path FROM media_manager_handoffs WHERE source_key = 'deluno' AND handoff_id = $id",
            ("$id", handoffId));
        Assert.True(ledger.Count > 0, $"no ledger row for {handoffId}");
        var columns = new List<(string Name, object? Value)>
        {
            ("library_id", ledger[0]["library_id"]),
            ("relative_path", relativePath ?? ledger[0]["relative_path"]),
            ("status", fileStatus),
        };
        if (file?.NextRetryAt is { } nextRetryAt)
        {
            columns.Add(("next_retry_at", nextRetryAt));
        }

        if (file?.StatusReason is { } statusReason)
        {
            columns.Add(("status_reason", statusReason));
        }

        if (file?.FailureAttempts is { } failureAttempts)
        {
            columns.Add(("failure_attempts", failureAttempts));
        }

        // An upsert: a server may already hold a Files row for a handed-over file that exists on disk, since it records its size on receipt.
        var updates = string.Join(", ", columns.Skip(2).Select(column => $"{column.Name} = excluded.{column.Name}"));
        SeedSql.Execute(
            connection,
            $"INSERT INTO files ({string.Join(", ", columns.Select(column => column.Name))}) "
                + $"VALUES ({string.Join(", ", columns.Select(column => "$" + column.Name))}) "
                + $"ON CONFLICT (library_id, relative_path) DO UPDATE SET {updates}",
            [.. columns.Select(column => ("$" + column.Name, column.Value))]);
    }

    /// <summary>A timestamp as the schema stores it, for the file columns.</summary>
    public static string StoredTime(DateTime moment) => SeedSql.UtcText(moment);

    /// <summary>The first minute of a moment as the status API reports it: <c>2026-10-03T14:05</c>.</summary>
    public static string MinutePrefix(DateTime moment) => moment.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
}
