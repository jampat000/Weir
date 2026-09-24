using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Weir.Api.Http;
using Weir.Core.Auth;
using Weir.Core.Json;
using Weir.Core.Settings;
using Weir.Core.Time;
using Weir.Core.Validation;
using Weir.Infrastructure.Settings;

namespace Weir.Api.Endpoints;

/// <summary>The global pause: whether processing is paused, until when, and whether periodic scans keep running.</summary>
public static class SuitePauseEndpoints
{
    public static IEndpointRouteBuilder MapSuitePauseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapV1("GET", "/pause", GetPauseAsync);
        endpoints.MapV1("PUT", "/pause", PutPauseAsync);
        return endpoints;
    }

    private static async Task<ApiResult> GetPauseAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(await PauseOutAsync(request).ConfigureAwait(false));
    }

    private static async Task<ApiResult> PutPauseAsync(ApiRequest request)
    {
        var body = await request.ReadBodyAsync().ConfigureAwait(false);
        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        var issues = new ValidationIssues();
        var model = new BodyModel(body, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        var paused = model.Bool("paused", defaultValue: false, required: true);
        var minutes = model.OptionalInt("pause_for_minutes", ge: 1, le: 10080);
        var scanWhilePaused = model.Bool("scan_while_paused", defaultValue: true);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        request.ValidateBrowserPostOrigin();
        var secret = request.RequireSessionSecret();
        if (!request.VerifyCsrf(secret, csrfToken, allowAnonymous: false))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "Invalid or expired CSRF token.");
        }

        var uow = await request.DbAsync().ConfigureAwait(false);
        var row = await SuiteSettingsStore.EnsureAsync(uow).ConfigureAwait(false);
        PyDateTime? until = null;
        if (paused && minutes is { } m)
        {
            until = PyDateTime.FromUtc(PyDateTime.UtcNow(request.Time).AsUtc.AddMinutes(m));
        }

        var updated = row with { ProcessingPaused = paused, ScanWhilePaused = scanWhilePaused, ProcessingPausedUntil = until };
        await SuiteSettingsStore.UpdateAsync(uow, row, updated).ConfigureAwait(false);
        return ApiRoutes.Ok(await PauseOutAsync(request).ConfigureAwait(false));
    }

    /// <summary>Resolve the pause, clearing a lapsed one so the row and the screen agree.</summary>
    private static async Task<PyDict> PauseOutAsync(ApiRequest request)
    {
        var uow = await request.DbAsync().ConfigureAwait(false);
        var row = await SuiteSettingsStore.EnsureAsync(uow).ConfigureAwait(false);
        var state = PauseState.Resolve(row, PyDateTime.UtcNow(request.Time).AsUtc);
        if (state.Expired)
        {
            await SuiteSettingsStore.UpdateAsync(uow, row, row with { ProcessingPaused = false, ProcessingPausedUntil = null }).ConfigureAwait(false);
        }

        return state.ToOut();
    }
}
