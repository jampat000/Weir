using System.Globalization;
using System.Numerics;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Weir.Api.Http;
using Weir.Core.Activity;
using Weir.Core.Auth;
using Weir.Core.Json;
using Weir.Core.Time;
using Weir.Core.Validation;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Settings;

namespace Weir.Api.Endpoints;

/// <summary>
/// The Activity feed: recent history with filters, export, one file's history and its removal, and the
/// live freshness stream.
/// </summary>
public static class ActivityEndpoints
{
    /// <summary>The reconnect delay the stream sends as its <c>retry:</c> hint.</summary>
    public const int StreamRetryMilliseconds = 5000;

    /// <summary>How long the stream stays quiet before it sends a keepalive comment.</summary>
    public static readonly TimeSpan StreamKeepalive = TimeSpan.FromSeconds(16);

    /// <summary>How long the stream waits before trying its first read again when it fails.</summary>
    public static readonly TimeSpan StreamReadRetry = TimeSpan.FromSeconds(2);

    public static IEndpointRouteBuilder MapActivityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var handlers = endpoints.ServiceProvider.GetRequiredService<ActivityEndpointHandlers>();
        endpoints.MapV1("GET", "/activity/recent", handlers.GetRecentAsync);
        endpoints.MapV1("GET", "/activity/stream", handlers.GetStreamAsync);
        endpoints.MapV1("GET", "/activity/export", handlers.GetExportAsync);
        endpoints.MapV1("GET", "/activity/file-history", handlers.GetFileHistoryAsync);
        endpoints.MapV1("POST", "/activity/file-history/remove", handlers.PostFileHistoryRemoveAsync);
        return endpoints;
    }

    /// <summary>
    /// Authenticate once with a short-lived connection, then stream <c>activity.latest</c> frames and, once a second at
    /// most, a <c>processing.progress</c> frame with every file's live progress (#750), plus keepalives, without
    /// holding the database.
    /// </summary>
    public static async IAsyncEnumerable<string> LatestFramesAsync(
        Func<CancellationToken, Task<long?>> readLatestId,
        ActivityLatestNotifier notifier,
        TimeProvider time,
        TimeSpan keepalive,
        ILogger logger,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readLatestId);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        yield return string.Create(CultureInfo.InvariantCulture, $"retry: {StreamRetryMilliseconds}\n\n");

        // Taken before the read, so a write that lands during it still reaches this stream.
        var lastSeenVersion = notifier.Snapshot().Version;
        var lastSentId = await ReadLatestIdAsync(readLatestId, time, logger, cancellationToken).ConfigureAwait(false);
        if (lastSentId is { } openingId)
        {
            yield return ActivityHistory.LatestEventFrame(openingId, lastSeenVersion);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            if (await notifier.WaitForChangeAsync(lastSeenVersion, keepalive, time, cancellationToken).ConfigureAwait(false) is not { } changed)
            {
                yield return ": keepalive\n\n";
                continue;
            }

            lastSeenVersion = changed.Version;
            lastSentId = lastSentId is { } sent && changed.LatestId is { } heard ? Math.Max(sent, heard) : lastSentId ?? changed.LatestId;
            if (lastSentId is { } latest)
            {
                yield return ActivityHistory.LatestEventFrame(latest, lastSeenVersion);
            }
        }
    }

    /// <summary>The newest event id when the stream opens, trying again after <see cref="StreamReadRetry"/> until it can be read.</summary>
    private static async Task<long?> ReadLatestIdAsync(
        Func<CancellationToken, Task<long?>> readLatestId,
        TimeProvider time,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                return await readLatestId(cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // A failed read must not end the live stream; it backs off and tries again.
            catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogWarning(exception, "The live Activity stream could not read the newest event; trying again shortly.");
            }

            await Task.Delay(StreamReadRetry, time, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>Handlers for <see cref="ActivityEndpoints"/>, constructor-injected with the stores they need.</summary>
internal sealed class ActivityEndpointHandlers
{
    private const string FormatPattern = "^(csv|json)$";

    private readonly ActivityHistoryStore _history;
    private readonly SuiteSettingsStore _suiteSettings;
    private readonly ActivityProgressFrames _progressFrames;

    public ActivityEndpointHandlers(ActivityHistoryStore history, SuiteSettingsStore suiteSettings, ActivityProgressFrames progressFrames)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _suiteSettings = suiteSettings ?? throw new ArgumentNullException(nameof(suiteSettings));
        _progressFrames = progressFrames ?? throw new ArgumentNullException(nameof(progressFrames));
    }

    public async Task<ApiResult> GetRecentAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var limit = QueryInt(request, "limit", issues, ge: 1, le: ActivityHistory.RecentMaxLimit) ?? ActivityHistory.RecentDefaultLimit;
        var module = QueryStr(request, "module", issues, 1, 32);
        var eventType = QueryStr(request, "event_type", issues, 1, 64);
        var search = QueryStr(request, "search", issues, 1, 200);
        var dateFrom = request.Query("date_from");
        var dateTo = request.Query("date_to");
        var beforeId = QueryInt(request, "before_id", issues, ge: 1);
        var trigger = QueryStr(request, "trigger", issues, 1, 32);
        var result = QueryStr(request, "result", issues, 1, 16);
        var libraryId = QueryInt(request, "library_id", issues, ge: 1);
        var file = QueryStr(request, "file", issues, 1, 2000);
        var about = QueryStr(request, "about", issues, 1, 16);
        issues.ThrowIfAny();

        var filter = new ActivityFilter(module, eventType, search, ParseWhen(dateFrom, "date_from"), ParseWhen(dateTo, "date_to"), trigger, result, libraryId, file, about);
        var uow = await request.DbAsync().ConfigureAwait(false);
        var page = await _history.ListRecentAsync(uow, filter, limit, beforeId).ConfigureAwait(false);
        // Only the first page is counted: a count walks every matching row, and later pages know whether more
        // remain from the page itself (#714).
        long? total = beforeId is null ? await _history.CountAsync(uow, filter).ConfigureAwait(false) : null;
        var settings = await _suiteSettings.EnsureAsync(uow).ConfigureAwait(false);
        var oldest = await _history.OldestCreatedAtAsync(uow).ConfigureAwait(false);
        return ApiRoutes.Ok(ActivityHistory.RecentOut(page.Items, page.HasMore, total, settings.ActivityRetentionDays, oldest));
    }

    public async Task<ApiResult> GetExportAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var format = "csv";
        if (request.Query("format") is { } rawFormat)
        {
            if (rawFormat is "csv" or "json")
            {
                format = rawFormat;
            }
            else
            {
                issues.Add(new ValidationIssue(
                    "string_pattern_mismatch",
                    ["query", "format"],
                    $"String should match pattern '{FormatPattern}'",
                    new WireString(rawFormat),
                    new WireObject().Set("pattern", FormatPattern)));
            }
        }

        var module = QueryStr(request, "module", issues, 1, 32);
        var eventType = QueryStr(request, "event_type", issues, 1, 64);
        var search = QueryStr(request, "search", issues, 1, 200);
        var dateFrom = request.Query("date_from");
        var dateTo = request.Query("date_to");
        var trigger = QueryStr(request, "trigger", issues, 1, 32);
        var result = QueryStr(request, "result", issues, 1, 16);
        var libraryId = QueryInt(request, "library_id", issues, ge: 1);
        var file = QueryStr(request, "file", issues, 1, 2000);
        var about = QueryStr(request, "about", issues, 1, 16);
        issues.ThrowIfAny();

        var filter = new ActivityFilter(module, eventType, search, ParseWhen(dateFrom, "date_from"), ParseWhen(dateTo, "date_to"), trigger, result, libraryId, file, about);
        var uow = await request.DbAsync().ConfigureAwait(false);
        var rows = await _history.ListForExportAsync(uow, filter).ConfigureAwait(false);
        var localNow = request.Time.GetLocalNow();
        var json = format == "json";
        var text = json ? ActivityHistory.ExportJson(rows) : ActivityHistory.ExportCsv(rows);
        var fileName = ActivityHistory.ExportFileName(localNow, json ? "json" : "csv");
        return new CustomApiResult(async context =>
        {
            context.Response.Headers["X-Weir-Export-Limit"] = ActivityHistory.ExportMaxRows.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers["X-Weir-Export-Rows"] = rows.Count.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
            await ApiResponses.WritePlainTextAsync(context, StatusCodes.Status200OK, text, json ? "application/json" : "text/csv; charset=utf-8").ConfigureAwait(false);
        });
    }

    public async Task<ApiResult> GetFileHistoryAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        string? relativePath = null;
        if (request.Query("relative_path") is not null)
        {
            relativePath = QueryStr(request, "relative_path", issues, 1, 2000);
        }
        else
        {
            issues.Add(FieldRules.Missing(["query", "relative_path"], WireNull.Instance));
        }

        var libraryId = QueryInt(request, "library_id", issues, ge: 1);
        issues.ThrowIfAny();

        var path = WireStrings.Strip(relativePath!);
        var uow = await request.DbAsync().ConfigureAwait(false);
        var counts = await _history.CountFileHistoryAsync(uow, libraryId, path).ConfigureAwait(false);
        return ApiRoutes.Ok(ActivityHistory.FileHistoryCountOut(counts));
    }

    public async Task<ApiResult> PostFileHistoryRemoveAsync(ApiRequest request)
    {
        var body = await request.ReadBodyAsync().ConfigureAwait(false);
        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        var issues = new ValidationIssues();
        var model = new BodyModel(body, issues);
        var libraryId = model.OptionalInt("library_id", ge: 1);
        var relativePath = model.Str("relative_path", minLength: 1, maxLength: 2000);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        model.Finish(ExtraFields.Ignore);
        issues.ThrowIfAny();

        request.RequireConfirmationToken(csrfToken);
        var path = WireStrings.Strip(relativePath);
        var uow = await request.DbAsync().ConfigureAwait(false);
        var deleted = await _history.DeleteFileHistoryAsync(uow, libraryId, path).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(ActivityHistory.FileHistoryRemoveOut(deleted));
    }

    public async Task<ApiResult> GetStreamAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        await request.ReleaseDbAsync().ConfigureAwait(false);
        var database = request.Database;
        var notifier = ActivityNotifications.For(database);
        var progressFrames = _progressFrames;
        var time = request.Time;
        var logger = request.LoggerFactory.CreateLogger("weir.platform.activity.router");
        return new CustomApiResult(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store, no-cache";
            context.Response.Headers.Connection = "keep-alive";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            // One write at a time: the two frame sources run concurrently, and a chunk must reach the client whole.
            var writeGate = new SemaphoreSlim(1, 1);
            async Task WriteFrameAsync(string chunk)
            {
                await writeGate.WaitAsync(context.RequestAborted).ConfigureAwait(false);
                try
                {
                    await context.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(chunk), context.RequestAborted).ConfigureAwait(false);
                    await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
                }
                finally
                {
                    writeGate.Release();
                }
            }

            async Task PumpAsync(IAsyncEnumerable<string> frames)
            {
                await foreach (var chunk in frames.ConfigureAwait(false))
                {
                    await WriteFrameAsync(chunk).ConfigureAwait(false);
                }
            }

            try
            {
                var activityLoop = PumpAsync(ActivityEndpoints.LatestFramesAsync(
                    ct => _history.LatestIdAsync(database, ct),
                    notifier,
                    time,
                    ActivityEndpoints.StreamKeepalive,
                    logger,
                    context.RequestAborted));
                var progressLoop = PumpAsync(progressFrames.ForAsync(time, context.RequestAborted));
                await Task.WhenAll(activityLoop, progressLoop).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The client went away.
            }
        });
    }

    /// <summary>An ISO 8601 date query, or 400 <c>Invalid {name}.</c> (an empty value is no filter).</summary>
    private static Timestamp? ParseWhen(string? raw, string name)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        return Timestamp.TryFromIsoFormat(raw, out var value)
            ? value
            : throw new ApiException(StatusCodes.Status400BadRequest, $"Invalid {name}.");
    }

    private static string? QueryStr(ApiRequest request, string name, ValidationIssues issues, int minLength, int maxLength)
    {
        var raw = request.Query(name);
        if (raw is null)
        {
            return null;
        }

        return FieldRules.TryStr(new WireString(raw), ["query", name], minLength, maxLength, issues, out var value) ? value : null;
    }

    private static long? QueryInt(ApiRequest request, string name, ValidationIssues issues, long? ge = null, long? le = null)
    {
        var raw = request.Query(name);
        if (raw is null || !FieldRules.TryInt(new WireString(raw), ["query", name], ge, le, issues, out var value))
        {
            return null;
        }

        return value > long.MaxValue ? long.MaxValue : (long)BigInteger.Max(value, long.MinValue);
    }
}
