using Microsoft.Extensions.Logging;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Time;
using Weir.Infrastructure.Scheduling;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>
/// Asks one media manager whether it is there, and says what happened in plain words (the connection test, shared by
/// the Test button and the heartbeat).
/// </summary>
public static class ManagerHealthProbe
{
    /// <summary>Where each kind answers a liveness check.</summary>
    private static readonly Dictionary<string, string> HealthPaths = new(StringComparer.Ordinal)
    {
        ["radarr"] = "/api/v3/system/status",
        ["sonarr"] = "/api/v3/system/status",
        ["deluno"] = "/api/integrations/external/health",
        ["native"] = "/api/integrations/external/health",
    };

    /// <summary><c>_probe</c>.</summary>
    public static async Task<(bool Ok, string Detail)> ProbeAsync(
        IManagerHttpHandlerFactory handlers, string name, string kind, string baseUrl, string? apiKey, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        if (PyStrings.Strip(baseUrl).Length == 0)
        {
            return (false, "Add the address where this app can be reached, then test again.");
        }

        var path = HealthPaths.GetValueOrDefault(kind, "/api/integrations/external/health");
        try
        {
            var client = new MediaManagerHttpClient(baseUrl, apiKey ?? string.Empty, handlers, timeout);
            await client.HealthOkAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (MediaManagerHttpException exception)
        {
            var detail = exception.Message;
            if (detail.Contains("HTTP 401", StringComparison.Ordinal) || detail.Contains("HTTP 403", StringComparison.Ordinal))
            {
                return (false, $"Weir reached {name}, but the API key was refused. Check the key and save it again.");
            }

            return (false, $"Weir reached {name} but did not get the answer it expected. Check the address points at the app itself, not a page inside it.");
        }
        catch (MediaManagerUnreachableException)
        {
            return (false, $"Weir could not reach {name} at {baseUrl}. Check the address is right, and that the app is running and reachable from this machine.");
        }

        return (true, $"Connected. Weir can reach {name}.");
    }
}

/// <summary>
/// The media manager heartbeat (James, 23 Sep 2026): every minute Weir runs each enabled manager's connection test and
/// saves the answer, so everything that depends on a manager — the Libraries list, the Media managers screen, and the
/// work that waits for a manager's word — knows within a minute that one has gone quiet or come back, and can say so
/// in plain words. Before this the saved answer was only as fresh as the last time someone pressed Test.
/// </summary>
public sealed class ManagerHeartbeatTask(
    SqliteDatabase database,
    MediaManagerConnectionService connections,
    IManagerHttpHandlerFactory handlers,
    TimeProvider time,
    ILogger<ManagerHeartbeatTask> logger) : IPeriodicTask
{
    /// <summary>How long one manager may take to answer before the heartbeat counts it as not there.</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    public string Name => "media-manager-heartbeat";

    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public bool RunAtStart => true;

    public TimeSpan? FailureCooldown => null;

    public string FailureMessage => "Media manager heartbeat failed.";

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        List<MediaManagerConnectionRecord> rows;
        var read = await UnitOfWork.OpenAsync(database, cancellationToken).ConfigureAwait(false);
        await using (read.ConfigureAwait(false))
        {
            rows = await MediaManagerConnectionStore.ListAsync(read).ConfigureAwait(false);
        }

        foreach (var row in rows.Where(r => r.Enabled))
        {
            var apiKey = string.IsNullOrEmpty(row.ApiKeyCiphertext) ? null : connections.Cipher.Decrypt(row.ApiKeyCiphertext);
            var (ok, detail) = await ManagerHealthProbe.ProbeAsync(handlers, row.Name, row.Kind, row.BaseUrl, apiKey, ProbeTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (row.LastTestOk is { } before && before != ok)
            {
                logger.LogInformation("{Name}: {Detail}", row.Name, detail);
            }

            // One short write per manager, never held across a probe: a manager that takes its full timeout must not
            // keep the database locked for the others.
            var write = await UnitOfWork.OpenAsync(database, cancellationToken).ConfigureAwait(false);
            await using (write.ConfigureAwait(false))
            {
                await MediaManagerConnectionStore.RecordTestResultAsync(write, row.Id, ok, PyDateTime.UtcNow(time), detail).ConfigureAwait(false);
                await write.CommitAsync().ConfigureAwait(false);
            }
        }
    }
}
