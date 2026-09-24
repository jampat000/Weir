using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Weir.Api.Http;
using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Core.Security;
using Weir.Core.Validation;
using Weir.Infrastructure.Activity;

namespace Weir.Api.Endpoints;

/// <summary>Changing the signed-in user's own username or password.</summary>
public static class AuthAccountEndpoints
{
    public static IEndpointRouteBuilder MapAuthAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapV1("POST", "/auth/change-username", PostChangeUsernameAsync);
        endpoints.MapV1("POST", "/auth/change-password", PostChangePasswordAsync);
        return endpoints;
    }

    private static async Task<ApiResult> PostChangeUsernameAsync(ApiRequest request)
    {
        var body = await request.ReadBodyAsync().ConfigureAwait(false);
        var user = await request.RequireUserAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var model = new BodyModel(body, issues);
        var currentPassword = model.Str("current_password", minLength: 1);
        var newUsername = model.Str("new_username", minLength: 1, maxLength: 64);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        var secret = request.RequireSessionSecret();
        request.ValidateBrowserPostOrigin();
        if (!request.VerifyCsrf(secret, csrfToken, allowAnonymous: false))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, AuthEndpoints.InvalidCsrf);
        }

        var uow = await request.DbAsync().ConfigureAwait(false);
        string changed;
        try
        {
            changed = await request.Auth.ChangeUsernameAsync(uow, user.User.Id, currentPassword, newUsername).ConfigureAwait(false);
        }
        catch (PyValueErrorException exception)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, exception.Message);
        }

        AuthEndpoints.Logger(request).LogInformation("auth event: username changed (user_id={UserId})", user.User.Id);
        await ActivityStore.RecordAsync(uow, ActivityEventTypes.AuthUsernameChanged, "auth", "Username changed", $"Signed in as {changed}.").ConfigureAwait(false);
        return ApiRoutes.Ok(new PyDict()
            .Set("message", "Username changed. Use it the next time you sign in.")
            .Set("username", changed));
    }

    private static async Task<ApiResult> PostChangePasswordAsync(ApiRequest request)
    {
        var body = await request.ReadBodyAsync().ConfigureAwait(false);
        var user = await request.RequireUserAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var model = new BodyModel(body, issues);
        var currentPassword = model.Str("current_password", minLength: 1);
        var newPassword = model.Str("new_password", minLength: PasswordPolicy.MinPasswordLength, maxLength: 512);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        var secret = request.RequireSessionSecret();
        request.ValidateBrowserPostOrigin();
        if (!request.VerifyCsrf(secret, csrfToken, allowAnonymous: false))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, AuthEndpoints.InvalidCsrf);
        }

        var uow = await request.DbAsync().ConfigureAwait(false);
        try
        {
            await request.Auth.ChangePasswordAsync(uow, user.User.Id, currentPassword, newPassword).ConfigureAwait(false);
        }
        catch (PyValueErrorException exception)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, exception.Message);
        }

        AuthEndpoints.Logger(request).LogInformation("auth event: password changed (user_id={UserId})", user.User.Id);
        await ActivityStore.RecordAsync(uow, ActivityEventTypes.AuthPasswordChanged, "auth", "Password changed", user.User.Username).ConfigureAwait(false);
        return ApiRoutes.Ok(new PyDict().Set("message", "Password changed. Sign in again with your new password."));
    }
}
