using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Weir.Api.Http;
using Weir.Core.Activity;
using Weir.Core.Auth;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Time;
using Weir.Core.Validation;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Sqlite;
using static Weir.Api.Endpoints.EndpointLookups;

namespace Weir.Api.Endpoints;

/// <summary>
/// Media manager connections and capabilities, the intake webhook and hand-off status, and system reconciliation.
/// </summary>
public static class MediaManagerEndpoints
{
    private const string InvalidCsrf = "Invalid or expired CSRF token.";

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>System reconciliation: the report and its repair.</summary>
    public static IEndpointRouteBuilder MapReconciliationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapV1("GET", "/system/reconciliation", GetReconciliationAsync);
        endpoints.MapV1("POST", "/system/reconciliation/repair", PostReconciliationRepairAsync);
        return endpoints;
    }

    /// <summary>The intake routes, then the connection routes.</summary>
    public static IEndpointRouteBuilder MapMediaManagerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Intake: the webhook and hand-off status.
        endpoints.MapV1("POST", "/intake/webhook/{source_key}", PostWebhookAsync);
        endpoints.MapV1("GET", "/intake/capabilities", GetIntakeCapabilitiesAsync);
        endpoints.MapV1("GET", "/intake/handoffs/{source_key}/{handoff_id}", GetHandoffAsync);
        endpoints.MapV1("DELETE", "/intake/handoffs/{source_key}/{handoff_id}", DeleteHandoffAsync);
        endpoints.MapV1("POST", "/intake/handoffs/{source_key}/{handoff_id}/outcome", PostHandoffOutcomeAsync);

        // Connections.
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

    // --- connections ---------------------------------------------------------------------------

    /// <summary>Browser origin, session secret, then a session-bound CSRF token; 400 on a bad token.</summary>
    private static void VerifyCsrf(ApiRequest request, string? token)
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
        return ApiRoutes.Ok(new PyList(rows.Select(row => (PyJson)row.ToOut())));
    }

    /// <summary>A <c>str</c> field with a default: absent means the default, present must be a string.</summary>
    private static string StrWithDefault(BodyModel model, PyJson? body, string name, string defaultValue, int? maxLength) =>
        body is PyDict dict && dict.ContainsKey(name) ? model.Str(name, maxLength: maxLength) : defaultValue;

    private static async Task<ApiResult> CreateConnectionAsync(ApiRequest request)
    {
        var body = await request.ReadBodyAsync().ConfigureAwait(false);
        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        var issues = new ValidationIssues();
        var model = new BodyModel(body, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        var kind = model.Literal("kind", MediaManagerKinds.All);
        var name = model.Str("name", minLength: 1, maxLength: 200);
        var enabled = model.Bool("enabled", defaultValue: true);
        var baseUrl = StrWithDefault(model, body, "base_url", string.Empty, 2000);
        var apiKey = StrWithDefault(model, body, "api_key", string.Empty, 2000);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        VerifyCsrf(request, csrfToken);
        var uow = await request.DbAsync().ConfigureAwait(false);
        long id;
        try
        {
            id = await Connections(request).CreateAsync(uow, kind, name, baseUrl, apiKey.Length > 0 ? apiKey : null, enabled).ConfigureAwait(false);
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
        var output = new PyList();
        foreach (var described in await Connections(request).DescribeConnectionsAsync(uow, request.Context.RequestAborted).ConfigureAwait(false))
        {
            var capabilities = described.Capabilities;
            output.Items.Add(new PyDict()
                .Set("connection_id", described.Connection.ConnectionId ?? 0)
                .Set("kind", described.Connection.Kind)
                .Set("name", described.Connection.Name)
                .Set("label", described.Connection.Label)
                .Set("media_scopes", new PyList(capabilities.Scopes.Order(StringComparer.Ordinal).Select(scope => (PyJson)new PyStr(scope))))
                .Set("reports_import_queue", capabilities.ReportsQueue)
                .Set("reports_library_truth", capabilities.ReportsLibraryTruth)
                .Set("reachable", described.Status == SignalStatus.Reported)
                .Set("library_roots", new PyList(described.LibraryRoots.Select(root => (PyJson)new PyStr(root))))
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
        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        var issues = new ValidationIssues();
        var connectionId = ConnectionId(request, issues);
        var model = new BodyModel(body, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        var name = model.OptionalStr("name", minLength: 1, maxLength: 200);
        var enabled = model.OptionalBool("enabled");
        var baseUrl = model.OptionalStr("base_url", maxLength: 2000);
        var apiKey = model.OptionalStr("api_key", maxLength: 2000);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        VerifyCsrf(request, csrfToken);
        var uow = await request.DbAsync().ConfigureAwait(false);
        var row = await RequireConnectionAsync(uow, connectionId).ConfigureAwait(false);
        try
        {
            await Connections(request).UpdateAsync(uow, row, name, baseUrl, apiKey, enabled).ConfigureAwait(false);
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
            PyResponses.NoContentJson(context);
            return Task.CompletedTask;
        });
    }

    private static async Task<ApiResult> PostWebhookSecretAsync(ApiRequest request)
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
        var plaintext = await Connections(request).RotateWebhookSecretAsync(uow, row).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(new PyDict()
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
        PydanticRules.TryLiteral(new PyStr(request.RouteValue("lane") ?? string.Empty), ["path", "lane"], MediaManagerKinds.SearchLanes, issues, out var lane);
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
        catch (PyValueErrorException exception)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, exception.Message);
        }

        // #544 item 2: a bad time (`25:00`, `9`) answers 400 naming the field that could not be read, not a 500.
        string start;
        try
        {
            start = ScheduleCsv.NormalizeHhmm(scheduleStart, "00:00");
        }
        catch (PyValueErrorException exception)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, $"schedule_start: {exception.Message}");
        }

        string end;
        try
        {
            end = ScheduleCsv.NormalizeHhmm(scheduleEnd, "23:59");
        }
        catch (PyValueErrorException exception)
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
        var checkedAt = PyDateTime.UtcNow(request.Time);

        bool ok;
        string detail;
        if (PyStrings.Strip(row.BaseUrl).Length == 0)
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
        return ApiRoutes.Ok(new PyDict()
            .Set("connection_id", row.Id)
            .Set("ok", ok)
            .Set("detail", detail)
            .Set("checked_at", checkedAt.PydanticJson()));
    }

    // --- intake --------------------------------------------------------------------------------

    private static MediaManagerIntake Intake(ApiRequest request) => request.Service<MediaManagerIntake>();

    private static async Task<T> RefusalsAsApiErrors<T>(Func<Task<T>> work)
    {
        try
        {
            return await work().ConfigureAwait(false);
        }
        catch (IntakeRefusedException exception)
        {
            throw new ApiException(exception.StatusCode, exception.Message);
        }
    }

    private static async Task<ApiResult> PostWebhookAsync(ApiRequest request)
    {
        var body = await request.ReadBodyAsync().ConfigureAwait(false);
        var sourceKey = request.RouteValue("source_key") ?? string.Empty;
        var presented = request.FirstHeader("X-Webhook-Secret");
        var issues = new ValidationIssues();
        PyDict payload = new();
        if (body is null or PyNull)
        {
            issues.Add(PydanticRules.Missing(["body"], PyNull.Instance));
        }
        else
        {
            PydanticRules.TryDict(body, ["body"], issues, out payload);
        }

        issues.ThrowIfAny();

        var dialect = ImportEvents.DialectForSource(sourceKey)
            ?? throw new ApiException(StatusCodes.Status404NotFound, IntakeRules.UnknownSourceDetail(sourceKey));
        var uow = await request.DbAsync().ConfigureAwait(false);
        var signed = await RefusalsAsApiErrors(() => Intake(request).AuthoriseAsync(uow, dialect.Key, presented)).ConfigureAwait(false);

        var importEvent = dialect.Normalize(payload);
        if (importEvent is null)
        {
            return ApiRoutes.Ok(new PyDict().Set("status", "ignored").Set("source", dialect.Key));
        }

        if (importEvent.EventKind == MediaManagerImportEvent.Imported)
        {
            // #652: Sonarr's and Radarr's "imported" is heard now. A file Weir handed back is recorded, and Weir's copy
            // released when that is safe; anything else is answered as before and changes nothing.
            var imported = await request.Service<HandbackOutcomes>()
                .RecordManagerImportAsync(uow, importEvent, ManagerName(dialect.Key), signed).ConfigureAwait(false);
            if (!imported.Matched)
            {
                return ApiRoutes.Ok(new PyDict().Set("status", "ignored").Set("source", dialect.Key).Set("event", importEvent.EventKind));
            }

            await request.CommitAsync().ConfigureAwait(false);
            return ApiRoutes.Ok(new PyDict()
                .Set("status", "ok")
                .Set("source", dialect.Key)
                .Set("event", importEvent.EventKind)
                .Set("matched", true)
                .Set("released", imported.Released)
                .Set("message", imported.Message));
        }

        var enqueued = await RefusalsAsApiErrors(() => Intake(request).EnqueueRefineAsync(uow, importEvent)).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(new PyDict()
            .Set("status", "ok")
            .Set("source", dialect.Key)
            .Set("event", importEvent.EventKind)
            .Set("media_scope", importEvent.MediaScope)
            .Set("enqueued", enqueued));
    }

    /// <summary>The dialect key for a source, or 404.</summary>
    private static string Source(string sourceKey) =>
        ImportEvents.DialectForSource(sourceKey)?.Key
        ?? throw new ApiException(StatusCodes.Status404NotFound, IntakeRules.UnknownSourceShortDetail(sourceKey));

    private static async Task<ApiResult> GetIntakeCapabilitiesAsync(ApiRequest request)
    {
        var presented = request.FirstHeader("X-Webhook-Secret");
        var uow = await request.DbAsync().ConfigureAwait(false);
        await RefusalsAsApiErrors(async () =>
        {
            await Intake(request).RequireSecretAsync(uow, presented, null).ConfigureAwait(false);
            return 0;
        }).ConfigureAwait(false);
        return ApiRoutes.Ok(new PyDict().Set("capabilities", new PyList(IntakeRules.HandoffCapabilities.Select(c => (PyJson)new PyStr(c)))));
    }

    private static async Task<(UnitOfWork Uow, string Key, HandoffLedgerRow Row)> RequireHandoffAsync(ApiRequest request)
    {
        var sourceKey = request.RouteValue("source_key") ?? string.Empty;
        var handoffId = request.RouteValue("handoff_id") ?? string.Empty;
        var presented = request.FirstHeader("X-Webhook-Secret");
        var key = Source(sourceKey);
        var uow = await request.DbAsync().ConfigureAwait(false);
        await RefusalsAsApiErrors(async () =>
        {
            await Intake(request).RequireSecretAsync(uow, presented, key).ConfigureAwait(false);
            return 0;
        }).ConfigureAwait(false);
        var row = await HandoffLedgerStore.FindAsync(uow, key, handoffId).ConfigureAwait(false)
            ?? throw new ApiException(StatusCodes.Status404NotFound, IntakeRules.NeverReceivedDetail);
        return (uow, key, row);
    }

    private static async Task<ApiResult> GetHandoffAsync(ApiRequest request)
    {
        var (uow, _, row) = await RequireHandoffAsync(request).ConfigureAwait(false);
        var answer = await Intake(request).Ledger.CurrentStatusAsync(uow, row).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(answer.AsJson());
    }

    private static async Task<ApiResult> DeleteHandoffAsync(ApiRequest request)
    {
        var (uow, key, row) = await RequireHandoffAsync(request).ConfigureAwait(false);
        var intake = Intake(request);
        var (cancelled, sentence) = await intake.Ledger.CancelAsync(uow, intake.Jobs, row).ConfigureAwait(false);
        if (!cancelled)
        {
            await uow.RollbackAsync().ConfigureAwait(false);
            throw new ApiException(StatusCodes.Status409Conflict, sentence);
        }

        await SqliteActivityWriter.RecordAsync(uow, new ActivityEventDraft(
            ActivityEventTypes.ProcessingHandoffCancelled,
            "processing",
            IntakeRules.CancelledTitle(key, row.RelativePath, OperatingSystem.IsWindows()),
            IntakeRules.CancelledDetail(key, row.HandoffId, row.RelativePath, row.LibraryId, sentence))).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        return new CustomApiResult(context =>
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
    }

    /// <summary>The manager's name as History and Activity say it: Sonarr, Radarr, Deluno.</summary>
    private static string ManagerName(string sourceKey) =>
        ImportEvents.DialectForSource(sourceKey) is { Key: not "native" } dialect ? dialect.DisplayName : "Your media manager";

    /// <summary>
    /// <c>POST /intake/handoffs/{source_key}/{handoff_id}/outcome</c> (#652, agreed with Deluno): the manager
    /// says what became of the file Weir handed back. <c>imported</c> records it and releases Weir's copy when that is safe;
    /// <c>not-imported</c> is final, and records it and keeps the copy. The same outcome sent again gets the same 200; a
    /// hand-off never received is 404; one not finished, or with a different outcome already recorded, is 409; a body
    /// that cannot be read is 422. Authenticated by <c>X-Webhook-Secret</c>, like the other hand-off routes.
    /// </summary>
    private static async Task<ApiResult> PostHandoffOutcomeAsync(ApiRequest request)
    {
        var body = await request.ReadBodyAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var model = new BodyModel(body, issues);
        var outcome = model.Literal("outcome", HandbackRules.Outcomes);
        var occurred = model.OptionalDateTime("occurredUtc");
        var importedPath = model.OptionalStr("importedPath", maxLength: 4000);
        var reason = model.OptionalStr("reason", maxLength: 2000);
        model.Finish(ExtraFields.Ignore);
        if (body is PyDict dict)
        {
            if (!dict.TryGetValue("occurredUtc", out var raw))
            {
                issues.Add(PydanticRules.Missing(["body", "occurredUtc"], dict));
            }
            else if (raw is PyNull)
            {
                issues.Add(new ValidationIssue("datetime_type", ["body", "occurredUtc"], "Input should be a valid datetime", raw));
            }
            else if (occurred is { IsAware: false })
            {
                issues.Add(new ValidationIssue("timezone_aware", ["body", "occurredUtc"], "Input should have timezone info", raw));
            }
        }

        issues.ThrowIfAny();

        var (uow, key, row) = await RequireHandoffAsync(request).ConfigureAwait(false);
        var manager = ManagerName(key);
        if (row.Outcome is { } recorded)
        {
            if (recorded != outcome)
            {
                throw new ApiException(
                    StatusCodes.Status409Conflict,
                    recorded == HandbackRules.Imported
                        ? $"{manager} already said it imported this file, so Weir kept that answer."
                        : $"{manager} already said it will not import this file, so Weir kept that answer.");
            }

            return ApiRoutes.Ok(OutcomeOut(row.HandoffId, recorded, row.OutcomeReleased, row.OutcomeMessage ?? string.Empty));
        }

        var status = await Intake(request).Ledger.CurrentStatusAsync(uow, row).ConfigureAwait(false);
        if (status.State is not (HandoffLedgerRules.Completed or HandoffLedgerRules.PassedThrough))
        {
            await request.CommitAsync().ConfigureAwait(false);
            throw new ApiException(
                StatusCodes.Status409Conflict,
                HandoffLedgerRules.TerminalStates.Contains(status.State)
                    ? $"This hand-off ended {status.State}, so Weir handed back no file to import."
                    : $"Weir has not finished this hand-off yet (it is {status.State}), so there is no file to import.");
        }

        var result = await request.Service<HandbackOutcomes>().RecordHandoffOutcomeAsync(
            uow, row, manager, outcome, occurred!.Value.AsUtc, importedPath, reason).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(OutcomeOut(row.HandoffId, outcome, result.Released, result.Message));
    }

    private static PyDict OutcomeOut(string handoffId, string outcome, bool released, string message) => new PyDict()
        .Set("handoffId", handoffId)
        .Set("outcome", outcome)
        .Set("released", released)
        .Set("message", message);

    // --- reconciliation ------------------------------------------------------------------------

    private static async Task<ApiResult> GetReconciliationAsync(ApiRequest request)
    {
        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        var uow = await request.DbAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(await ReconciliationService.BuildReportAsync(uow).ConfigureAwait(false));
    }

    /// <summary>
    /// Runs a reconciliation repair. Like every other operator POST it requires the browser origin check and a
    /// session-bound <c>csrf_token</c> (#527), answering 403 for a bad origin and 400
    /// <c>Invalid or expired CSRF token.</c> for a missing or wrong token.
    /// </summary>
    private static async Task<ApiResult> PostReconciliationRepairAsync(ApiRequest request)
    {
        var body = await request.ReadBodyAsync().ConfigureAwait(false);
        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        var issues = new ValidationIssues();
        var model = new BodyModel(body, issues);
        var action = model.Str("action", minLength: 1, maxLength: 100);
        var dbId = model.OptionalInt("db_id", ge: 1);
        var path = model.OptionalStr("path", maxLength: 2000);
        var confirm = model.Bool("confirm", defaultValue: false);
        var csrfToken = model.OptionalStr("csrf_token");
        model.Finish(ExtraFields.Ignore);
        issues.ThrowIfAny();

        VerifyCsrf(request, csrfToken);
        var uow = await request.DbAsync().ConfigureAwait(false);
        PyDict result;
        try
        {
            result = await ReconciliationService.RepairAsync(uow, action, dbId, path, confirm).ConfigureAwait(false);
        }
        catch (PyValueErrorException exception)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, exception.Message);
        }

        var applied = result.Get("applied") is { IsTruthy: true };
        var message = result.Get("message") is { } text ? PyConvert.Str(text) : string.Empty;
        await SqliteActivityWriter.RecordAsync(uow, new ActivityEventDraft(
            ActivityEventTypes.SystemReconciliationRepair,
            "system",
            applied ? "System repair action completed" : "System repair action skipped",
            $"{action}: {message}")).ConfigureAwait(false);
        return ApiRoutes.Ok(result);
    }
}
