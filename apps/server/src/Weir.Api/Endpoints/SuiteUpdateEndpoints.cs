using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Weir.Api.Http;
using Weir.Core;
using Weir.Core.Auth;
using Weir.Core.Updates;
using Weir.Core.Validation;
using Weir.Infrastructure.Http;
using Weir.Infrastructure.Runtime;

namespace Weir.Api.Endpoints;

/// <summary>Update status, update settings and the apply-update flow.</summary>
public static class SuiteUpdateEndpoints
{
    public static IEndpointRouteBuilder MapSuiteUpdateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapV1("GET", "/suite/update-status", GetUpdateStatusAsync);
        endpoints.MapV1("GET", "/suite/update-settings", GetUpdateSettingsAsync);
        endpoints.MapV1("PUT", "/suite/update-settings", PutUpdateSettingsAsync);
        endpoints.MapV1("GET", "/suite/update-state", GetUpdateStateAsync);
        endpoints.MapV1("POST", "/suite/apply-update", PostApplyUpdateAsync);
        return endpoints;
    }

    private static async Task<ApiResult> GetUpdateStatusAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var installType = UpdateFiles.DetectInstallType(request.Options.RuntimeKind);
        var currentVersion = WeirVersion.Resolve(request.Options.VersionOverride);
        if (currentVersion.Length == 0)
        {
            currentVersion = "0.0.0";
        }

        try
        {
            var release = await request.Service<IReleaseCatalogClient>().FetchLatestAsync(currentVersion, request.Context.RequestAborted).ConfigureAwait(false);
            return ApiRoutes.Ok(UpdateStatus.FromRelease(currentVersion, installType, release));
        }
        catch (ReleaseFetchException exception) when (exception.StatusCode == 404)
        {
            return ApiRoutes.Ok(UpdateStatus.Unavailable(currentVersion, installType, "not_published", "No public Weir release is published yet."));
        }
#pragma warning disable CA1031 // An update check that fails for any reason reads as unavailable, never an error page.
        catch (Exception exception) when (exception is not OperationCanceledException || !request.Context.RequestAborted.IsCancellationRequested)
#pragma warning restore CA1031
        {
            return ApiRoutes.Ok(UpdateStatus.Unavailable(currentVersion, installType, "unavailable", "Could not check for updates right now."));
        }
    }

    private static async Task<ApiResult> GetUpdateSettingsAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var logger = request.LoggerFactory.CreateLogger("weir.platform.suite_settings.update_service");
        return ApiRoutes.Ok(request.Service<UpdateFiles>().ReadSettings(path => logger.LogWarning(
            "Update settings at {Path} could not be read. Falling back to notify-only so a damaged file cannot install an update the operator did not choose.",
            path)));
    }

    private static async Task<ApiResult> PutUpdateSettingsAsync(ApiRequest request)
    {
        var body = await request.ReadBodyAsync().ConfigureAwait(false);
        await request.RequireUserAsync(UserRoles.AdminOnly).ConfigureAwait(false);
        var issues = new ValidationIssues();
        var model = new BodyModel(body, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        var mode = model.Literal("mode", UpdateStatus.Modes);
        var checkOnStartup = model.Bool("check_on_startup", defaultValue: true);
        var interval = model.Number("check_interval_minutes", 60, required: false, ge: 1, le: 10080);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        request.RequireConfirmationToken(csrfToken);
        return ApiRoutes.Ok(request.Service<UpdateFiles>().WriteSettings(mode, checkOnStartup, interval));
    }

    private static async Task<ApiResult> GetUpdateStateAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(request.Service<UpdateFiles>().ReadState());
    }

    private static async Task<ApiResult> PostApplyUpdateAsync(ApiRequest request)
    {
        var body = await request.ReadBodyAsync().ConfigureAwait(false);
        await request.RequireUserAsync(UserRoles.AdminOnly).ConfigureAwait(false);
        var issues = new ValidationIssues();
        var model = new BodyModel(body, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        request.RequireConfirmationToken(csrfToken);
        var files = request.Service<UpdateFiles>();
        var state = files.ReadState();
        if (!(state.Get("downloaded")?.IsTruthy ?? false))
        {
            throw new ApiException(StatusCodes.Status409Conflict, "No downloaded update is pending.");
        }

        files.WriteApplyFlag();
        return ApiRoutes.Ok(state);
    }
}
