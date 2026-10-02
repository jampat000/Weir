using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Weir.Api.Http;
using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Core.Logs;
using Weir.Core.Validation;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.SystemLog;

namespace Weir.Api.Endpoints;

/// <summary>
/// <c>GET /api/v1/system/log</c>: Weir's events, its jobs and its server log as one list, newest first, and
/// <c>GET /api/v1/system/log/export</c>: the same list, as a file, for the filters given.
/// </summary>
public static class SystemLogEndpoints
{
    public static IEndpointRouteBuilder MapSystemLogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var handlers = endpoints.ServiceProvider.GetRequiredService<SystemLogEndpointHandlers>();
        endpoints.MapV1("GET", "/system/log", handlers.GetLogAsync);
        endpoints.MapV1("GET", "/system/log/export", handlers.GetExportAsync);
        return endpoints;
    }
}

/// <summary>Handlers for <see cref="SystemLogEndpoints"/>.</summary>
internal sealed class SystemLogEndpointHandlers
{
    private const string ExportFormatPattern = "^(csv|json)$";

    private readonly SystemLogReader _log;

    public SystemLogEndpointHandlers(SystemLogReader log) => _log = log ?? throw new ArgumentNullException(nameof(log));

    public async Task<ApiResult> GetLogAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var query = SystemLogQuery.Read(request, issues);
        issues.ThrowIfAny();

        var uow = await request.DbAsync().ConfigureAwait(false);
        var page = await _log.ReadAsync(uow, query.Filter, query.After, query.Limit).ConfigureAwait(false);
        var names = await WorkflowNamesAsync(uow, page.Rows).ConfigureAwait(false);
        return ApiRoutes.Ok(SystemLogWire.Page(page, names));
    }

    public async Task<ApiResult> GetExportAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var json = ExportAsJson(request, issues);
        var query = SystemLogQuery.Read(request, issues);
        issues.ThrowIfAny();

        var uow = await request.DbAsync().ConfigureAwait(false);
        var page = await _log.ReadAsync(uow, query.Filter, null, ActivityHistory.ExportMaxRows).ConfigureAwait(false);
        var names = await WorkflowNamesAsync(uow, page.Rows).ConfigureAwait(false);
        var text = json ? SystemLogWire.Json(page.Rows, names) : SystemLogWire.Csv(page.Rows, names);
        var fileName = SystemLogWire.ExportFileName(request.Time.GetLocalNow(), json ? "json" : "csv");
        return new CustomApiResult(async context =>
        {
            context.Response.Headers["X-Weir-Export-Limit"] = ActivityHistory.ExportMaxRows.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers["X-Weir-Export-Rows"] = page.Rows.Count.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
            await ApiResponses.WritePlainTextAsync(context, StatusCodes.Status200OK, text, json ? "application/json" : "text/csv; charset=utf-8").ConfigureAwait(false);
        });
    }

    private static Task<IReadOnlyDictionary<long, string>> WorkflowNamesAsync(UnitOfWork uow, IReadOnlyList<SystemLogRow> rows) =>
        SystemLogReader.WorkflowNamesAsync(uow, [.. rows.Select(row => row.WorkflowId).OfType<long>().Distinct()]);

    private static bool ExportAsJson(ApiRequest request, ValidationIssues issues)
    {
        var format = request.Query("format");
        if (format is null || format is "csv")
        {
            return false;
        }

        if (format is "json")
        {
            return true;
        }

        issues.Add(new ValidationIssue(
            "string_pattern_mismatch",
            ["query", "format"],
            $"String should match pattern '{ExportFormatPattern}'",
            new WireString(format),
            new WireObject().Set("pattern", ExportFormatPattern)));
        return false;
    }
}
