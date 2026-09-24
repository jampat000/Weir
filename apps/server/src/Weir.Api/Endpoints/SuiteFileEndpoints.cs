using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Weir.Api.Http;
using Weir.Core.Auth;
using Weir.Core.Validation;
using Weir.Infrastructure.Logging;
using Weir.Infrastructure.Settings;

namespace Weir.Api.Endpoints;

/// <summary>
/// Files Weir writes when asked: a configuration snapshot right now (System › Backups' "Back up now"), and the
/// whole server log as a download for a support request (System › Logs).
/// </summary>
public static class SuiteFileEndpoints
{
    private const string LogMediaType = "application/x-ndjson; charset=utf-8";

    public static IEndpointRouteBuilder MapSuiteFileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapV1("POST", "/suite/configuration-backups", PostBackupAsync);
        endpoints.MapV1("GET", "/suite/logs/download", DownloadLogAsync);
        return endpoints;
    }

    private static async Task<ApiResult> PostBackupAsync(ApiRequest request)
    {
        var body = await request.ReadBodyAsync().ConfigureAwait(false);
        await request.RequireUserAsync(UserRoles.AdminOnly).ConfigureAwait(false);
        var issues = new ValidationIssues();
        var model = new BodyModel(body, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        request.RequireConfirmationToken(csrfToken);
        var uow = await request.DbAsync().ConfigureAwait(false);
        ConfigurationBackupRecord row;
        try
        {
            row = await request.Service<ConfigurationBackups>().CreateAsync(uow).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            request.LoggerFactory.CreateLogger("weir.platform.suite_settings.backups").LogWarning(exception, "A backup asked for by hand could not be written.");
            throw new ApiException(
                StatusCodes.Status500InternalServerError,
                "Weir could not write the backup file. Check that Weir can write to its backup folder, then try again.");
        }

        await request.CommitAsync().ConfigureAwait(false);
        return new JsonApiResult(StatusCodes.Status201Created, ConfigurationBackups.ItemOut(row));
    }

    private static async Task<ApiResult> DownloadLogAsync(ApiRequest request)
    {
        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        var logFile = request.Service<WeirLogFile>();
        var text = new StringBuilder();
        if (File.Exists(logFile.Path) && !logFile.ReadLines(line => text.Append(line).Append('\n')))
        {
            request.LoggerFactory.CreateLogger("weir.platform.suite_settings.logs_service").LogWarning("Log download skipped because the active log could not be opened.");
            throw new ApiException(StatusCodes.Status503ServiceUnavailable, "Weir could not open its log file just now. Try again in a moment.");
        }

        var fileName = string.Create(CultureInfo.InvariantCulture, $"weir-log-{request.Time.GetLocalNow():yyyyMMdd-HHmmss}.log");
        var content = text.ToString();
        return new CustomApiResult(async context =>
        {
            context.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
            await PyResponses.WritePlainTextAsync(context, StatusCodes.Status200OK, content, LogMediaType).ConfigureAwait(false);
        });
    }
}
