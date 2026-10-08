using Microsoft.Extensions.Logging;
using Weir.Core.Json;
using Weir.Infrastructure.Sqlite;

namespace Weir.Api.Endpoints;

/// <summary>
/// The <c>system.overview</c> side of the Activity stream: the facts System shows about this copy of Weir (its jobs and requests
/// today, its checks, the update, the browsers watching), sent when a stream opens and again each time one of them has changed.
/// Weir looks every few seconds for the stream; what has not changed sends nothing. Uptime counts every second and so never makes
/// a frame by itself: the page counts it from <c>started_at</c>.
/// </summary>
internal sealed class SystemOverviewFrames
{
    /// <summary>How often a stream looks for a change in the overview.</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(5);

    private readonly SystemOverviewReader _overview;
    private readonly SqliteDatabase _database;
    private readonly IServiceProvider _services;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    public SystemOverviewFrames(
        SystemOverviewReader overview, SqliteDatabase database, IServiceProvider services, TimeProvider time, ILoggerFactory loggers)
    {
        _overview = overview ?? throw new ArgumentNullException(nameof(overview));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        ArgumentNullException.ThrowIfNull(loggers);
        _logger = loggers.CreateLogger("weir.platform.system.overview");
    }

    /// <summary>The frames for one open stream, from when it opens until it ends.</summary>
    public async IAsyncEnumerable<string> ForAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? sent = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (await TryReadAsync(cancellationToken).ConfigureAwait(false) is { } overview)
            {
                var facts = WireJsonWriter.Dumps(WithoutUptime(overview), WireJsonFormat.Compact);
                if (facts != sent)
                {
                    sent = facts;
                    yield return Frame(overview);
                }
            }

            await Task.Delay(CheckEvery, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The <c>system.overview</c> SSE frame: the overview as <c>GET /system/overview</c> answers it.</summary>
    public static string Frame(WireObject overview)
    {
        ArgumentNullException.ThrowIfNull(overview);
        return $"event: system.overview\ndata: {WireJsonWriter.Dumps(overview, WireJsonFormat.Compact)}\n\n";
    }

    private static WireObject WithoutUptime(WireObject overview)
    {
        var facts = overview.Copy();
        facts.Remove("uptime_seconds");
        return facts;
    }

    /// <summary>The overview now, or null when it could not be read: the next look tries again.</summary>
    private async Task<WireObject?> TryReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
            await using (uow.ConfigureAwait(false))
            {
                return await _overview.ReadAsync(uow, _services, cancellationToken).ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // A read that fails must not end the live stream; the next look tries again.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogDebug(exception, "The live System overview could not be read; trying again shortly.");
            return null;
        }
    }
}
