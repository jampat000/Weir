using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Weir.Api.Http;
using Weir.Core.Auth;
using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Core.Validation;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Processing;
using static Weir.Api.Endpoints.EndpointLookups;

namespace Weir.Api.Endpoints;

/// <summary>Media-manager library discovery (#554): discover/drift/import against a connection's own
/// libraries, and forgetting where an imported library came from. Backed by
/// <see cref="LibraryDiscoveryService"/>.</summary>
public static class ProcessingLibraryDiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapProcessingLibraryDiscoveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapV1("GET", "/processing/libraries/discover/{connection_id}", GetDiscoverableLibrariesAsync);
        endpoints.MapV1("POST", "/processing/libraries/discover/{connection_id}/import", PostImportLibrariesAsync);
        endpoints.MapV1("GET", "/processing/libraries/discover/{connection_id}/drift", GetLibraryDriftAsync);
        endpoints.MapV1("POST", "/processing/libraries/{library_id}/unlink", PostLibraryUnlinkAsync);
        return endpoints;
    }

    private static PyDict DiscoverableLibraryOut(DiscoverableLibrary item) => new PyDict()
        .Set("key", item.Key)
        .Set("name", item.Name)
        .Set("media_type", item.MediaType)
        .Set("root_path", item.RootPath)
        .Set("already_imported", item.AlreadyImported)
        .Set("local_path_problem", item.LocalPathProblem)
        .Set("output_path", item.OutputPath)
        .Set("processes_before_import", item.ProcessesBeforeImport)
        .Set("output_path_problem", item.OutputPathProblem);

    private static PyDict LibraryDriftOut(LibraryDrift item) => new PyDict()
        .Set("kind", item.Kind)
        .Set("library_id", item.LibraryId)
        .Set("library_name", item.LibraryName)
        .Set("manager_value", item.ManagerValue)
        .Set("weir_value", item.WeirValue)
        .Set("detail", item.Detail);

    /// <summary><c>GET /processing/libraries/discover/{connection_id}</c>: what this manager says it looks
    /// after, and whether Weir already has it.</summary>
    private static async Task<ApiResult> GetDiscoverableLibrariesAsync(ApiRequest request)
    {
        var issues = new ValidationIssues();
        var connectionId = ConnectionId(request, issues);
        issues.ThrowIfAny();

        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);

        var uow = await request.DbAsync().ConfigureAwait(false);
        var connection = await RequireConnectionAsync(uow, connectionId).ConfigureAwait(false);
        List<DiscoverableLibrary> found;
        try
        {
            found = await request.Service<LibraryDiscoveryService>()
                .DiscoverableLibrariesAsync(uow, connection, request.Context.RequestAborted).ConfigureAwait(false);
        }
        catch (ProcessingDiscoveryException exception)
        {
            throw new ApiException(StatusCodes.Status502BadGateway, exception.Message);
        }

        return ApiRoutes.Ok(new PyList(found.Select(item => (PyJson)DiscoverableLibraryOut(item))));
    }

    /// <summary><c>POST /processing/libraries/discover/{connection_id}/import</c>: create a Processing library per
    /// selected manager library.</summary>
    private static async Task<ApiResult> PostImportLibrariesAsync(ApiRequest request)
    {
        var payload = await request.ReadBodyAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var connectionId = ConnectionId(request, issues);
        var model = new BodyModel(payload, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        var keys = model.StrList("keys", []);
        model.Finish(ExtraFields.Forbid);
        if (keys.Count == 0)
        {
            issues.Add(new ValidationIssue("too_short", ["body", "keys"], "List should have at least 1 item after validation, not 0", new PyList()));
        }

        issues.ThrowIfAny();

        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        // Every route in this file refuses a bad token with this exact wording, which clients match on.
        request.RequireConfirmationToken(csrfToken, "Invalid or expired CSRF token.");

        var uow = await request.DbAsync().ConfigureAwait(false);
        var connection = await RequireConnectionAsync(uow, connectionId).ConfigureAwait(false);
        List<ProcessingLibraryRecord> created;
        try
        {
            created = await request.Service<LibraryDiscoveryService>()
                .ImportLibrariesAsync(uow, connection, keys, request.Context.RequestAborted).ConfigureAwait(false);
        }
        catch (ProcessingDiscoveryException exception)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, exception.Message);
        }

        await request.CommitAsync().ConfigureAwait(false);
        request.Service<ScanSettingsChanges>().Record();
        var items = new List<PyJson>();
        foreach (var row in created)
        {
            items.Add(await ProcessingLibraryMapping.LibraryOutAsync(request, uow, row, request.Service<ScanWakeups>()).ConfigureAwait(false));
        }

        return new JsonApiResult(StatusCodes.Status201Created, new PyList(items));
    }

    /// <summary><c>GET /processing/libraries/discover/{connection_id}/drift</c>: differences between the manager
    /// and Weir. Reported only — nothing is applied.</summary>
    private static async Task<ApiResult> GetLibraryDriftAsync(ApiRequest request)
    {
        var issues = new ValidationIssues();
        var connectionId = ConnectionId(request, issues);
        issues.ThrowIfAny();

        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);

        var uow = await request.DbAsync().ConfigureAwait(false);
        var connection = await RequireConnectionAsync(uow, connectionId).ConfigureAwait(false);
        List<LibraryDrift> drift;
        try
        {
            drift = await request.Service<LibraryDiscoveryService>()
                .ResyncDriftAsync(uow, connection, request.Context.RequestAborted).ConfigureAwait(false);
        }
        catch (ProcessingDiscoveryException exception)
        {
            throw new ApiException(StatusCodes.Status502BadGateway, exception.Message);
        }

        return ApiRoutes.Ok(new PyList(drift.Select(item => (PyJson)LibraryDriftOut(item))));
    }

    /// <summary><c>POST /processing/libraries/{library_id}/unlink</c>: forget where a library came from. The
    /// library itself is untouched.</summary>
    private static async Task<ApiResult> PostLibraryUnlinkAsync(ApiRequest request)
    {
        var payload = await request.ReadBodyAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var id = request.PathInt("library_id", issues);
        var model = new BodyModel(payload, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        // Every route in this file refuses a bad token with this exact wording, which clients match on.
        request.RequireConfirmationToken(csrfToken, "Invalid or expired CSRF token.");

        var uow = await request.DbAsync().ConfigureAwait(false);
        var row = await RequireLibraryAsync(uow, id).ConfigureAwait(false);
        var updated = await LibraryDiscoveryService.UnlinkLibraryAsync(uow, row).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(await ProcessingLibraryMapping.LibraryOutAsync(request, uow, updated, request.Service<ScanWakeups>()).ConfigureAwait(false));
    }
}
