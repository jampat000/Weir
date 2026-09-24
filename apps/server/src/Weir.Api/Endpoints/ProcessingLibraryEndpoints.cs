using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Weir.Api.Http;
using Weir.Core.Auth;
using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Core.Validation;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.LibraryMode;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Processing;
using static Weir.Api.Endpoints.EndpointLookups;

namespace Weir.Api.Endpoints;

/// <summary>Processing libraries — <c>/api/v1/processing/libraries</c>. Manager coverage reads the linked
/// connections' saved test results (#520). Also the opt-in Reject failure policy's support gate
/// (<c>GET /processing/reject-support</c>, and the same check on save; #522 part 4), and the manager-setup
/// check used while a library is being created or edited. Media-manager library discovery (discover/drift/
/// import) and library unlink (#554) live in <see cref="ProcessingLibraryDiscoveryEndpoints"/>; rule sets
/// live in <see cref="ProcessingRuleSetsEndpoints"/>. Request/response mapping is shared via
/// <see cref="ProcessingLibraryMapping"/>.</summary>
public static class ProcessingLibraryEndpoints
{
    public static IEndpointRouteBuilder MapProcessingLibraryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapV1("GET", "/processing/libraries", GetLibrariesAsync);
        endpoints.MapV1("POST", "/processing/libraries", PostLibraryAsync);
        endpoints.MapV1("GET", "/processing/reject-support", GetRejectSupportAsync);
        endpoints.MapV1("GET", "/processing/manager-setup", GetManagerSetupAsync);
        endpoints.MapV1("GET", "/processing/libraries/{library_id}/folder-chain", GetLibraryFolderChainAsync);
        endpoints.MapV1("GET", "/processing/libraries/{library_id}", GetLibraryAsync);
        endpoints.MapV1("PUT", "/processing/libraries/{library_id}", PutLibraryAsync);
        endpoints.MapV1("DELETE", "/processing/libraries/{library_id}", DeleteLibraryAsync);
        endpoints.MapV1("POST", "/processing/libraries/reorder", PostReorderAsync);
        return endpoints;
    }

    private static async Task<ApiResult> GetLibrariesAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var uow = await request.DbAsync().ConfigureAwait(false);
        var rows = await LibraryStore.ListAsync(uow).ConfigureAwait(false);
        var items = new List<WireValue>();
        foreach (var row in rows)
        {
            items.Add(await ProcessingLibraryMapping.LibraryOutAsync(request, uow, row, request.Service<ScanWakeups>()).ConfigureAwait(false));
        }

        return ApiRoutes.Ok(new WireArray(items));
    }

    private static async Task<ApiResult> GetLibraryAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var id = request.PathInt("library_id", issues);
        issues.ThrowIfAny();
        var uow = await request.DbAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(await ProcessingLibraryMapping.LibraryOutAsync(request, uow, await RequireLibraryAsync(uow, id).ConfigureAwait(false), request.Service<ScanWakeups>()).ConfigureAwait(false));
    }

    /// <summary>
    /// <c>GET /api/v1/processing/reject-support</c>: whether the opt-in Reject failure policy can be chosen for a library
    /// linked to the given connections. Asks each manager's manifest (or its static capabilities, for one whose port
    /// removes queue items), so it is called when the option is shown, not on every list.
    /// </summary>
    private static async Task<ApiResult> GetRejectSupportAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var connectionIds = new List<long>();
        foreach (var raw in request.Context.Request.Query["connection_ids"])
        {
            if (raw is not null && long.TryParse(raw, out var id))
            {
                connectionIds.Add(id);
            }
        }

        var uow = await request.DbAsync().ConfigureAwait(false);
        var connections = await request.Service<MediaManagerConnectionService>().ConnectionsByIdAsync(uow, connectionIds).ConfigureAwait(false);
        var support = await request.Service<RejectSupportEvaluator>().EvaluateAsync(connections).ConfigureAwait(false);
        return ApiRoutes.Ok(new WireObject().Set("available", support.Available).Set("reason", support.Reason));
    }

    /// <summary>
    /// <c>GET /api/v1/processing/manager-setup</c>: for a library's media type and folders (saved or still being typed),
    /// what each enabled Sonarr, Radarr or Deluno connection needs, and whether it already has it — the remote path
    /// mapping Sonarr/Radarr must hold, or the folders Deluno reports. Read only: Weir never writes a manager's settings.
    /// </summary>
    private static async Task<ApiResult> GetManagerSetupAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var mediaType = ProcessingMediaScopes.Movie;
        if (request.Query("media_type") is not { } rawType)
        {
            issues.Add(new ValidationIssue("missing", ["query", "media_type"], "Field required", WireValue.Null));
        }
        else if (FieldRules.TryLiteral(new WireString(rawType), ["query", "media_type"], ProcessingMediaScopes.All, issues, out var parsedType))
        {
            mediaType = parsedType;
        }

        var watchedFolder = WireStrings.Slice(request.Query("watched_folder") ?? string.Empty, 4000);
        var outputFolder = WireStrings.Slice(request.Query("output_folder") ?? string.Empty, 4000);
        var removesOriginals = true;
        if (request.Query("remove_original_after_success") is { } rawRemove)
        {
            if (FieldRules.TryLiteral(new WireString(rawRemove.ToLowerInvariant()), ["query", "remove_original_after_success"], ["true", "false"], issues, out var parsedRemove))
            {
                removesOriginals = parsedRemove == "true";
            }
        }

        issues.ThrowIfAny();

        var uow = await request.DbAsync().ConfigureAwait(false);
        var managers = await request.Service<ManagerSetupCheck>()
            .CheckAsync(uow, mediaType, watchedFolder, outputFolder, removesOriginals, request.Context.RequestAborted)
            .ConfigureAwait(false);
        return ApiRoutes.Ok(new WireObject().Set("media_type", mediaType).Set("managers", new WireArray(managers.Select(item => (WireValue)item))));
    }

    /// <summary>
    /// <c>GET /api/v1/processing/libraries/{library_id}/folder-chain</c>: one library's folder chain — Weir's own
    /// watched/work/output folders, plus every enabled connection that covers its media type, folded into one plain-
    /// language, read-only view.
    /// </summary>
    private static async Task<ApiResult> GetLibraryFolderChainAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var id = request.PathInt("library_id", issues);
        issues.ThrowIfAny();

        var uow = await request.DbAsync().ConfigureAwait(false);
        var library = await RequireLibraryAsync(uow, id).ConfigureAwait(false);
        var chain = await request.Service<LibraryFolderChainCheck>()
            .CheckForLibraryAsync(uow, library, request.Context.RequestAborted)
            .ConfigureAwait(false);
        return ApiRoutes.Ok(chain);
    }

    private static async Task<ApiResult> PostLibraryAsync(ApiRequest request)
    {
        var payload = await request.ReadBodyAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var model = new BodyModel(payload, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        var body = ProcessingLibraryMapping.ReadLibraryBody(model);
        model.Finish(ExtraFields.Forbid);
        if (!issues.Any)
        {
            ProcessingLibraryMapping.ValidateDetectionWindows(body, issues);
        }

        issues.ThrowIfAny();

        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        request.RequireConfirmationToken(csrfToken);

        var uow = await request.DbAsync().ConfigureAwait(false);
        ProcessingLibraryRecord row;
        try
        {
            row = await LibraryStore.CreateAsync(uow, body, request.Options.WeirHome).ConfigureAwait(false);
        }
        catch (ProcessingLibraryException exception)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, exception.Message);
        }

        await ProcessingLibraryMapping.RefuseUnsupportedRejectAsync(request, uow, row).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        request.Service<ScanSettingsChanges>().Record();
        return new JsonApiResult(StatusCodes.Status201Created, await ProcessingLibraryMapping.LibraryOutAsync(request, uow, row, request.Service<ScanWakeups>()).ConfigureAwait(false));
    }

    private static async Task<ApiResult> PutLibraryAsync(ApiRequest request)
    {
        var payload = await request.ReadBodyAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var pathIssues = new ValidationIssues();
        var id = request.PathInt("library_id", pathIssues);
        var model = new BodyModel(payload, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        var body = ProcessingLibraryMapping.ReadLibraryBody(model);
        model.Finish(ExtraFields.Forbid);
        pathIssues.ThrowIfAny();
        if (!issues.Any)
        {
            ProcessingLibraryMapping.ValidateDetectionWindows(body, issues);
        }

        issues.ThrowIfAny();

        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        request.RequireConfirmationToken(csrfToken);

        var uow = await request.DbAsync().ConfigureAwait(false);
        var existing = await RequireLibraryAsync(uow, id).ConfigureAwait(false);
        ProcessingLibraryRecord updated;
        try
        {
            updated = await LibraryStore.UpdateAsync(uow, existing, body, request.Options.WeirHome).ConfigureAwait(false);
        }
        catch (ProcessingLibraryException exception)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, exception.Message);
        }

        await ProcessingLibraryMapping.RefuseUnsupportedRejectAsync(request, uow, updated).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        request.Service<ScanSettingsChanges>().Record();
        return ApiRoutes.Ok(await ProcessingLibraryMapping.LibraryOutAsync(request, uow, updated, request.Service<ScanWakeups>()).ConfigureAwait(false));
    }

    private static async Task<ApiResult> DeleteLibraryAsync(ApiRequest request)
    {
        var payload = await request.ReadBodyAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var id = request.PathInt("library_id", issues);
        var model = new BodyModel(payload, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        request.RequireConfirmationToken(csrfToken);

        var uow = await request.DbAsync().ConfigureAwait(false);
        var row = await RequireLibraryAsync(uow, id).ConfigureAwait(false);
        try
        {
            await LibraryStore.DeleteAsync(uow, row).ConfigureAwait(false);
        }
        catch (ProcessingLibraryException exception)
        {
            throw new ApiException(StatusCodes.Status409Conflict, exception.Message);
        }

        // #505: a deleted library takes its library-mode settings and scan history with it (both live on
        // jobs rows, not a foreign-keyed table — see docs/archive/server-port-notes.md, "Library mode").
        await request.Service<LibrarySettingsStore>().DeleteAllForLibraryAsync(uow, row.Id).ConfigureAwait(false);

        await request.CommitAsync().ConfigureAwait(false);
        request.Service<ScanSettingsChanges>().Record();
        return new CustomApiResult(context =>
        {
            ApiResponses.NoContentJson(context);
            return Task.CompletedTask;
        });
    }

    private static async Task<ApiResult> PostReorderAsync(ApiRequest request)
    {
        var payload = await request.ReadBodyAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var model = new BodyModel(payload, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        var orderedIds = model.IntList("library_ids_in_order");
        model.Finish(ExtraFields.Forbid);
        if (orderedIds.Count == 0)
        {
            issues.Add(new ValidationIssue("too_short", ["body", "library_ids_in_order"], "List should have at least 1 item after validation, not 0", new WireArray()));
        }

        issues.ThrowIfAny();

        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        request.RequireConfirmationToken(csrfToken);

        var uow = await request.DbAsync().ConfigureAwait(false);
        List<ProcessingLibraryRecord> rows;
        try
        {
            rows = await LibraryStore.ReorderAsync(uow, orderedIds).ConfigureAwait(false);
        }
        catch (ProcessingLibraryException exception)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, exception.Message);
        }

        await request.CommitAsync().ConfigureAwait(false);
        var items = new List<WireValue>();
        foreach (var row in rows)
        {
            items.Add(await ProcessingLibraryMapping.LibraryOutAsync(request, uow, row, request.Service<ScanWakeups>()).ConfigureAwait(false));
        }

        return ApiRoutes.Ok(new WireArray(items));
    }
}
