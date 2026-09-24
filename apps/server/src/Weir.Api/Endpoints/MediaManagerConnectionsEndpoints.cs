using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Weir.Api.Http;
using Weir.Core.Auth;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Time;
using Weir.Core.Validation;
using Weir.Infrastructure.MediaManagers;
using static Weir.Api.Endpoints.EndpointLookups;

namespace Weir.Api.Endpoints;

/// <summary>
/// Media manager connections and capabilities: create/list/edit/delete a connection, its search lanes, the
/// webhook secret it authenticates with, and the connection test also used by the heartbeat
/// (<see cref="ManagerHealthProbe"/>).
/// </summary>
public static class MediaManagerConnectionsEndpoints
{
    internal const string InvalidCsrf = "Invalid or expired CSRF token.";

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

    public static IEndpointRouteBuilder MapMediaManagerConnectionsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapV1("GET", "/media-managers/connections", ListConnectionsAsync);
        endpoints.MapV1("POST", "/media-managers/connections", CreateConnectionAsync);
        endpoints.MapV1("GET", "/media-managers/capabilities", GetCapabilitiesAsync);
        endpoints.MapV1("GET", "/media-managers/connections/{connection_id}", GetConnectionAsync);
        endpoints.MapV1("PUT", "/media-managers/connections/{connection_id}", UpdateConnectionAsync);
        endpoints.MapV1("DELETE", "/media-managers/connections/{connection_id}", DeleteConnectionAsync);
        endpoints.MapV1("POST", "/media-managers/connections/{connection_id}/webhook-secret", PostWebhookSecretAsync);
        endpoints.MapV1("PUT", "/media-managers/connections/{connection_id}/lanes/{lane}", PutLaneAsync);
        endpoints.MapV1("POST", "/media-managers/connections/{connection_id}/test", PostConnectionTestAsync);
        return endpoints;
    }

    /// <summary>Browser origin, session secret, then a session-bound CSRF token; 400 on a bad token. Shared
    /// with <see cref="MediaManagerReconciliationEndpoints"/>, which guards its repair route the same way.</summary>
    internal static void VerifyCsrf(ApiRequest request, string? token)
    {
        request.ValidateBrowserPostOrigin();
        var secret = request.RequireSessionSecret();
        if (!request.VerifyCsrf(secret, token, allowAnonymous: false))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, InvalidCsrf);
        }
    }

    private static MediaManagerConnectionService Connections(ApiRequest request) => request.Service<MediaManagerConnectionService>();

    private static async Task<ApiResult> ListConnectionsAsync(ApiRequest request)
    {
        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        var uow = await request.DbAsync().ConfigureAwait(false);
        var rows = await MediaManagerConnectionStore.ListAsync(uow).ConfigureAwait(false);
        return ApiRoutes.Ok(new WireArray(rows.Select(row => (WireValue)row.ToOut())));
    }

    /// <summary>A <c>str</c> field with a default: absent means the default, present must be a string.</summary>
    private static string StrWithDefault(BodyModel model, WireValue? body, string name, string defaultValue, int? maxLength) =>
        body is WireObject dict && dict.ContainsKey(name) ? model.Str(name, maxLength: maxLength) : defaultValue;

    private static async Task<ApiResult> CreateConnectionAsync(ApiRequest request)
    {
        var body = await request.ReadBodyAsync().ConfigureAwait(false);
        await request.RequireUserAsync(UserRoles.AdminOnly).ConfigureAwait(false);
        var issues = new ValidationIssues();
        var model = new BodyModel(body, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        var kind = model.Literal("kind", MediaManagerKinds.All);
        var name = model.Str("name", minLength: 1, maxLength: 200);
        var enabled = model.Bool("enabled", defaultValue: true);
        var baseUrl = StrWithDefault(model, body, "base_url", string.Empty, 2000);
        var apiKey = StrWithDefault(model, body, "api_key", string.Empty, 2000);
        var downloadedScanEnabled = model.Bool("downloaded_scan_enabled", defaultValue: false);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        VerifyCsrf(request, csrfToken);
        var uow = await request.DbAsync().ConfigureAwait(false);
        long id;
        try
        {
            id = await Connections(request)
                .CreateAsync(uow, kind, name, baseUrl, apiKey.Length > 0 ? apiKey : null, enabled, downloadedScanEnabled).ConfigureAwait(false);
        }
        catch (MediaManagerConnectionException exception)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, exception.Message);
        }

        await request.CommitAsync().ConfigureAwait(false);
        var row = await RequireConnectionAsync(uow, id).ConfigureAwait(false);
        return new JsonApiResult(StatusCodes.Status201Created, row.ToOut());
    }

    private static async Task<ApiResult> GetCapabilitiesAsync(ApiRequest request)
    {
        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        var uow = await request.DbAsync().ConfigureAwait(false);
        var output = new WireArray();
        foreach (var described in await Connections(request).DescribeConnectionsAsync(uow, request.Context.RequestAborted).ConfigureAwait(false))
        {
            var capabilities = described.Capabilities;
            output.Items.Add(new WireObject()
                .Set("connection_id", described.Connection.ConnectionId ?? 0)
                .Set("kind", described.Connection.Kind)
                .Set("name", described.Connection.Name)
                .Set("label", described.Connection.Label)
                .Set("media_scopes", new WireArray(capabilities.Scopes.Order(StringComparer.Ordinal).Select(scope => (WireValue)new WireString(scope))))
                .Set("reports_import_queue", capabilities.ReportsQueue)
                .Set("reports_library_truth", capabilities.ReportsLibraryTruth)
                .Set("reachable", described.Status == SignalStatus.Reported)
                .Set("library_roots", new WireArray(described.LibraryRoots.Select(root => (WireValue)new WireString(root))))
                .Set("summary", capabilities.Summary)
                .Set("detail", described.Detail));
        }

        return ApiRoutes.Ok(output);
    }

    private static async Task<ApiResult> GetConnectionAsync(ApiRequest request)
    {
        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        var issues = new ValidationIssues();
        var connectionId = ConnectionId(request, issues);
        issues.ThrowIfAny();
        var uow = await request.DbAsync().ConfigureAwait(false);
        return ApiRoutes.Ok((await RequireConnectionAsync(uow, connectionId).ConfigureAwait(false)).ToOut());
    }

    private static async Task<ApiResult> UpdateConnectionAsync(ApiRequest request)
    {
        var body = await request.ReadBodyAsync().ConfigureAwait(false);
        await request.RequireUserAsync(UserRoles.AdminOnly).ConfigureAwait(false);
        var issues = new ValidationIssues();
        var connectionId = ConnectionId(request, issues);
        var model = new BodyModel(body, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        var name = model.OptionalStr("name", minLength: 1, maxLength: 200);
        var enabled = model.OptionalBool("enabled");
        var baseUrl = model.OptionalStr("base_url", maxLength: 2000);
        var apiKey = model.OptionalStr("api_key", maxLength: 2000);
        var downloadedScanEnabled = model.OptionalBool("downloaded_scan_enabled");
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        VerifyCsrf(request, csrfToken);
        var uow = await request.DbAsync().ConfigureAwait(false);
        var row = await RequireConnectionAsync(uow, connectionId).ConfigureAwait(false);
        try
        {
            await Connections(request).UpdateAsync(uow, row, name, baseUrl, apiKey, enabled, downloadedScanEnabled).ConfigureAwait(false);
        }
        catch (MediaManagerConnectionException exception)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, exception.Message);
        }

        await request.CommitAsync().ConfigureAwait(false);
        return ApiRoutes.Ok((await RequireConnectionAsync(uow, connectionId).ConfigureAwait(false)).ToOut());
    }

    private static async Task<ApiResult> DeleteConnectionAsync(ApiRequest request)
    {
        var body = await request.ReadBodyAsync().ConfigureAwait(false);
        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        var issues = new ValidationIssues();
        var connectionId = ConnectionId(request, issues);
        var model = new BodyModel(body, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        VerifyCsrf(request, csrfToken);
        var uow = await request.DbAsync().ConfigureAwait(false);
        await RequireConnectionAsync(uow, connectionId).ConfigureAwait(false);
        await MediaManagerConnectionStore.DeleteAsync(uow, connectionId).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        return new CustomApiResult(context =>
        {
            ApiResponses.NoContentJson(context);
            return Task.CompletedTask;
        });
    }

    private static async Task<ApiResult> PostWebhookSecretAsync(ApiRequest request)
    {
        var body = await request.ReadBodyAsync().ConfigureAwait(false);
        await request.RequireUserAsync(UserRoles.AdminOnly).ConfigureAwait(false);
        var issues = new ValidationIssues();
        var connectionId = ConnectionId(request, issues);
        var model = new BodyModel(body, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        VerifyCsrf(request, csrfToken);
        var uow = await request.DbAsync().ConfigureAwait(false);
        var row = await RequireConnectionAsync(uow, connectionId).ConfigureAwait(false);
        var plaintext = await Connections(request).RotateWebhookSecretAsync(uow, row).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(new WireObject()
            .Set("connection_id", row.Id)
            .Set("webhook_secret", plaintext)
            .Set("webhook_url_path", row.WebhookUrlPath)
            .Set("header_name", "X-Webhook-Secret"));
    }

    private static async Task<ApiResult> PutLaneAsync(ApiRequest request)
    {
        var body = await request.ReadBodyAsync().ConfigureAwait(false);
        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        var issues = new ValidationIssues();
        var connectionId = ConnectionId(request, issues);
        FieldRules.TryLiteral(new WireString(request.RouteValue("lane") ?? string.Empty), ["path", "lane"], MediaManagerKinds.SearchLanes, issues, out var lane);
        var model = new BodyModel(body, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        var enabled = model.Bool("enabled", defaultValue: false, required: true);
        var maxItems = model.Number("max_items_per_run", 0, required: true, ge: 1, le: 1000);
        var retryDelay = model.Number("retry_delay_minutes", 0, required: true, ge: 1, le: 525600);
        var scheduleEnabled = model.Bool("schedule_enabled", defaultValue: false, required: true);
        var scheduleDays = model.Str("schedule_days", maxLength: 2000);
        var scheduleStart = model.Str("schedule_start", maxLength: 5);
        var scheduleEnd = model.Str("schedule_end", maxLength: 5);
        var interval = model.Number("schedule_interval_seconds", 0, required: true, ge: 60, le: 604800);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        VerifyCsrf(request, csrfToken);
        var uow = await request.DbAsync().ConfigureAwait(false);
        await RequireConnectionAsync(uow, connectionId).ConfigureAwait(false);
        string days;
        try
        {
            days = ScheduleCsv.ValidateScheduleDaysCsv(scheduleDays);
        }
        catch (WireValueException exception)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, exception.Message);
        }

        // #544 item 2: a bad time (`25:00`, `9`) answers 400 naming the field that could not be read, not a 500.
        string start;
        try
        {
            start = ScheduleCsv.NormalizeHhmm(scheduleStart, "00:00");
        }
        catch (WireValueException exception)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, $"schedule_start: {exception.Message}");
        }

        string end;
        try
        {
            end = ScheduleCsv.NormalizeHhmm(scheduleEnd, "23:59");
        }
        catch (WireValueException exception)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, $"schedule_end: {exception.Message}");
        }

        var saved = await MediaManagerConnectionStore.SaveLaneAsync(uow, new MediaManagerSearchLaneRecord(
            0,
            connectionId,
            lane,
            enabled,
            maxItems,
            retryDelay,
            scheduleEnabled,
            days,
            start,
            end,
            interval)).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(saved.ToOut());
    }

    /// <summary>The connection test, shared with the heartbeat (<see cref="ManagerHealthProbe"/>).</summary>
    private static Task<(bool Ok, string Detail)> ProbeAsync(ApiRequest request, string name, string kind, string baseUrl, string? apiKey) =>
        ManagerHealthProbe.ProbeAsync(
            request.Service<IManagerHttpHandlerFactory>(), name, kind, baseUrl, apiKey, TestTimeout, request.Context.RequestAborted);

    private static async Task<ApiResult> PostConnectionTestAsync(ApiRequest request)
    {
        var body = await request.ReadBodyAsync().ConfigureAwait(false);
        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        var issues = new ValidationIssues();
        var connectionId = ConnectionId(request, issues);
        var model = new BodyModel(body, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        VerifyCsrf(request, csrfToken);
        var uow = await request.DbAsync().ConfigureAwait(false);
        var row = await RequireConnectionAsync(uow, connectionId).ConfigureAwait(false);
        var checkedAt = Timestamp.UtcNow(request.Time);

        bool ok;
        string detail;
        if (WireStrings.Strip(row.BaseUrl).Length == 0)
        {
            (ok, detail) = (false, "Add the address where this app can be reached, then test again.");
        }
        else
        {
            var apiKey = string.IsNullOrEmpty(row.ApiKeyCiphertext) ? null : Connections(request).Cipher.Decrypt(row.ApiKeyCiphertext);
            (ok, detail) = await ProbeAsync(request, row.Name, row.Kind, row.BaseUrl, apiKey).ConfigureAwait(false);
        }

        // The probe can take seconds and the connection may be removed meanwhile, so the write is conditional.
        if (await MediaManagerConnectionStore.RecordTestResultAsync(uow, connectionId, ok, checkedAt, detail).ConfigureAwait(false) == 0)
        {
            await uow.RollbackAsync().ConfigureAwait(false);
            throw new ApiException(StatusCodes.Status404NotFound, "That media manager connection was removed while its connection test was running.");
        }

        await request.CommitAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(new WireObject()
            .Set("connection_id", row.Id)
            .Set("ok", ok)
            .Set("detail", detail)
            .Set("checked_at", checkedAt.ToWireText()));
    }
}
