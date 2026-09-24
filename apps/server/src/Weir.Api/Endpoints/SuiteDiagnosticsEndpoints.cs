using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Weir.Api.Http;
using Weir.Core.Auth;
using Weir.Core.Json;
using Weir.Core.Logs;
using Weir.Core.Metrics;
using Weir.Core.Validation;
using Weir.Infrastructure.Browse;
using Weir.Infrastructure.Logging;
using Weir.Infrastructure.Media;

namespace Weir.Api.Endpoints;

/// <summary>The local directory browser, the media-tools report, the suite log viewer and runtime metrics.</summary>
public static class SuiteDiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapSuiteDiagnosticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Local directory browser.
        endpoints.MapV1("GET", "/system/directories", GetDirectoriesAsync);

        // #548: the external media tools this install actually has.
        endpoints.MapV1("GET", "/system/media-tools", GetMediaToolsAsync);

        endpoints.MapV1("GET", "/suite/logs", GetLogsAsync);
        endpoints.MapV1("GET", "/suite/metrics", GetMetricsAsync);
        return endpoints;
    }

    /// <summary>
    /// #548: which external media tools this install has, and what they say they are. Weir bundles both in the
    /// Windows package (<c>server\bin\ffmpeg</c>, <c>server\bin\mkvtoolnix</c>) and the Docker image, but a
    /// source install provides its own. This lets an operator see whether mkvmerge, the one that matters most for a
    /// library's writer setting, was found: the writer defaults to "best", and "best" silently means ffmpeg wherever
    /// mkvmerge is missing, so without this an install with no mkvmerge looks identical to one that uses it.
    ///
    /// <para>
    /// Always 200. <c>mkvmerge: "not installed"</c> is the correct answer for an install without it, not a
    /// failure — the writer falls back to ffmpeg and everything still works, which is the whole reason
    /// <see cref="IMediaToolResolver.ResolveMkvmerge"/> returns null instead of throwing. A missing ffmpeg
    /// reports the same way rather than erroring, because an operator diagnosing exactly that needs the answer,
    /// not a 500.
    /// </para>
    /// </summary>
    private static async Task<ApiResult> GetMediaToolsAsync(ApiRequest request)
    {
        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        var report = await request.Service<MediaTools>()
            .DescribeVersionsAsync(request.Context.RequestAborted)
            .ConfigureAwait(false);
        return ApiRoutes.Ok(new WireObject()
            .Set("ffmpeg", report.Ffmpeg)
            .Set("mkvmerge", report.Mkvmerge));
    }

    private static async Task<ApiResult> GetDirectoriesAsync(ApiRequest request)
    {
        await request.RequireUserAsync(UserRoles.AdminOnly).ConfigureAwait(false);
        try
        {
            return ApiRoutes.Ok(DirectoryBrowser.Browse(request.Query("path")));
        }
        catch (DirectoryBrowseException exception)
        {
            throw new ApiException(exception.StatusCode, exception.Message);
        }
    }

    private static async Task<ApiResult> GetLogsAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var level = request.Query("level");
        var search = request.Query("search");
        bool? hasException = null;
        if (request.Query("has_exception") is { } rawHasException && FieldRules.TryBool(new WireString(rawHasException), ["query", "has_exception"], issues, out var parsedHasException))
        {
            hasException = parsedHasException;
        }

        long limit = 100;
        if (request.Query("limit") is { } rawLimit && FieldRules.TryInt(new WireString(rawLimit), ["query", "limit"], null, null, issues, out var parsedLimit))
        {
            limit = parsedLimit > long.MaxValue ? long.MaxValue : parsedLimit < long.MinValue ? long.MinValue : (long)parsedLimit;
        }

        issues.ThrowIfAny();
        var filter = new SuiteLogFilter(level, search, hasException, limit);
        var logFile = request.Service<WeirLogFile>();
        var result = SuiteLogFilter.Empty;
        if (File.Exists(logFile.Path))
        {
            if (logFile.ReadLines(filter.Add))
            {
                result = filter.Result();
            }
            else
            {
                request.LoggerFactory.CreateLogger("weir.platform.suite_settings.logs_service").LogWarning("Suite log read skipped because the active log could not be opened.");
            }
        }

        var items = new List<WireValue>();
        var logger = request.LoggerFactory.CreateLogger("weir.platform.suite_settings.router");
        foreach (var entry in result.Items)
        {
            var output = SuiteLogFilter.ToOut(entry);
            if (output is null)
            {
                logger.LogWarning("Skipping suite log entry with invalid timestamp: {Timestamp}", entry.Timestamp);
                continue;
            }

            items.Add(output);
        }

        return ApiRoutes.Ok(new WireObject()
            .Set("items", new WireArray(items))
            .Set("total", result.Total)
            .Set("counts", new WireObject().Set("error", result.Errors).Set("warning", result.Warnings).Set("information", result.Information)));
    }

    private static async Task<ApiResult> GetMetricsAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(request.Service<RuntimeMetricsStore>().SuiteMetricsOut());
    }
}
