using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Weir.Api.Http;
using Weir.Core.Activity;
using Weir.Core.Auth;
using Weir.Core.Json;
using Weir.Core.Text;
using Weir.Core.Validation;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Auth;

namespace Weir.Api.Endpoints;

/// <summary>The signed-in user's own account and session inventory: who am I, which sessions are active, and revoking them.</summary>
public static class AuthSessionEndpoints
{
    public static IEndpointRouteBuilder MapAuthSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapV1("GET", "/auth/me", GetMeAsync);
        endpoints.MapV1("GET", "/auth/session", GetSessionAsync);
        endpoints.MapV1("GET", "/auth/sessions", GetSessionsAsync);
        endpoints.MapV1("POST", "/auth/sessions/revoke-others", PostRevokeOthersAsync);
        endpoints.MapV1("POST", "/auth/sessions/{session_id}/revoke", PostRevokeSessionAsync);
        endpoints.MapV1("GET", "/auth/admin/ping", GetAdminPingAsync);
        return endpoints;
    }

    private static async Task<ApiResult> GetMeAsync(ApiRequest request)
    {
        var current = await request.RequireUserAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(new PyDict().Set("user", AuthService.UserPublic(current.User)));
    }

    private static async Task<ApiResult> GetSessionAsync(ApiRequest request)
    {
        var uow = await request.DbAsync().ConfigureAwait(false);
        var pair = await request.Auth.LoadValidSessionAsync(uow, request.RawSessionToken).ConfigureAwait(false)
            ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Not authenticated.");
        return ApiRoutes.Ok(request.Auth.SessionPublic(pair.Session, current: true));
    }

    private static async Task<ApiResult> GetSessionsAsync(ApiRequest request)
    {
        var user = await request.RequireUserAsync().ConfigureAwait(false);
        var uow = await request.DbAsync().ConfigureAwait(false);
        var pair = await request.Auth.LoadValidSessionAsync(uow, request.RawSessionToken).ConfigureAwait(false)
            ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Not authenticated.");
        // Prefer the row RequireUserAsync already loaded: it may carry this request's last-seen touch.
        var current = user.Session.Id == pair.Session.Id ? user.Session : pair.Session;
        var items = await request.Auth.ListActiveSessionsAsync(uow, user.User.Id, current).ConfigureAwait(false);
        return ApiRoutes.Ok(new PyDict().Set("items", new PyList(items)));
    }

    private static async Task<ApiResult> PostRevokeOthersAsync(ApiRequest request)
    {
        var user = await request.RequireUserAsync().ConfigureAwait(false);
        var headerToken = request.FirstHeader("X-CSRF-Token");
        request.ValidateBrowserPostOrigin();
        var secret = request.RequireSessionSecret();
        if (headerToken is null || !request.VerifyCsrf(secret, headerToken, allowAnonymous: false))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, AuthEndpoints.ConfirmationExpired);
        }

        var uow = await request.DbAsync().ConfigureAwait(false);
        var pair = await request.Auth.LoadValidSessionAsync(uow, request.RawSessionToken).ConfigureAwait(false)
            ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Not authenticated.");
        var count = await request.Auth.RevokeOtherUserSessionsAsync(uow, user.User.Id, pair.Session.Id).ConfigureAwait(false);
        await ActivityStore.RecordAsync(
            uow, ActivityEventTypes.AuthSessionsRevoked, "auth", "Other sessions signed out",
            count.ToString(System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(new PyDict().Set("message", $"Signed out {Plural.Of(count, "other session")}.").Set("revoked_count", count));
    }

    private static async Task<ApiResult> PostRevokeSessionAsync(ApiRequest request)
    {
        var user = await request.RequireUserAsync().ConfigureAwait(false);
        var headerToken = request.FirstHeader("X-CSRF-Token");
        var issues = new ValidationIssues();
        PydanticRules.TryUuid(request.RouteValue("session_id") ?? string.Empty, ["path", "session_id"], issues, out var sessionId);
        issues.ThrowIfAny();

        request.ValidateBrowserPostOrigin();
        var secret = request.RequireSessionSecret();
        if (headerToken is null || !request.VerifyCsrf(secret, headerToken, allowAnonymous: false))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, AuthEndpoints.ConfirmationExpired);
        }

        var uow = await request.DbAsync().ConfigureAwait(false);
        var pair = await request.Auth.LoadValidSessionAsync(uow, request.RawSessionToken).ConfigureAwait(false)
            ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Not authenticated.");
        if (sessionId.ToString("N") == pair.Session.Id)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "The current session cannot be revoked here.");
        }

        if (!await request.Auth.RevokeUserSessionAsync(uow, user.User.Id, sessionId).ConfigureAwait(false))
        {
            throw new ApiException(StatusCodes.Status404NotFound, "Session is no longer active.");
        }

        await ActivityStore.RecordAsync(uow, ActivityEventTypes.AuthSessionsRevoked, "auth", "Session signed out", "One other session was revoked.").ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(new PyDict().Set("message", "Session signed out.").Set("revoked_count", 1));
    }

    private static async Task<ApiResult> GetAdminPingAsync(ApiRequest request)
    {
        await request.RequireUserAsync(UserRoles.AdminOnly).ConfigureAwait(false);
        return ApiRoutes.Ok(new PyDict().Set("ok", true));
    }
}
