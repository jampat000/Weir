using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Weir.Api.Http;
using Weir.Core.Auth;
using Weir.Core.Json;
using Weir.Core.Library;
using Weir.Core.MediaManagers;
using Weir.Core.Rules;
using Weir.Core.Validation;
using Weir.Infrastructure.LibraryMode;
using Weir.Infrastructure.MediaManagers;
using static Weir.Api.Endpoints.EndpointLookups;

namespace Weir.Api.Endpoints;

/// <summary>
/// Library mode (#509): titles a library's current rules would now keep a track for that a past clean removed for
/// good, and the "Download again" action that asks the file's media manager to fetch it once more. See
/// <see cref="LibraryModeEndpoints"/> for the rest of the Library mode surface.
/// </summary>
public static class LibraryModeRedownloadsEndpoints
{
    public static IEndpointRouteBuilder MapLibraryModeRedownloadsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapV1("GET", "/processing/libraries/{library_id}/library-redownloads", GetRedownloadsAsync);
        endpoints.MapV1("POST", "/processing/libraries/{library_id}/library-redownloads", PostRedownloadAsync);
        return endpoints;
    }

    private static PyDict RemovedTrackOut(RemovedTrackRecord track) => new PyDict()
        .Set("language", track.Language)
        .Set("type", track.Type == RemovedTrackType.Audio ? "audio" : "subtitle")
        .Set("codec", track.Codec)
        .Set("variant", track.Variant)
        .Set("reason", track.Reason);

    /// <summary>
    /// #509: titles a library's *current* rules would now keep a track for that a past clean removed for good — "12
    /// titles are missing tracks your new rules keep" (issue #509 step 2's diff), scoped to this library. The
    /// "Download again" action <see cref="PostRedownloadAsync"/> offers is gated on <c>can_redownload</c>
    /// (<see cref="ManagerRedownloadRules.CanRedownload"/>): true only for a manager kind issue #509 verified
    /// (Sonarr/Radarr) <em>and</em> a file #551's title matching actually resolved to one of that manager's titles.
    /// </summary>
    private static async Task<ApiResult> GetRedownloadsAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var libraryId = request.PathInt("library_id", issues);
        issues.ThrowIfAny();

        var uow = await request.DbAsync().ConfigureAwait(false);
        var library = await RequireLibraryAsync(uow, libraryId, LibraryModeMapping.NoLibraryWithThatId).ConfigureAwait(false);
        var rules = await LibraryModeMapping.RulesForAsync(uow, library).ConfigureAwait(false);

        var removedTrackStore = request.Service<IRemovedTrackStore>();
        var allRemoved = await removedTrackStore.GetAllAsync().ConfigureAwait(false);
        var forLibrary = allRemoved.Where(kv => kv.Key.LibraryId == libraryId).ToDictionary(kv => kv.Key, kv => kv.Value);
        var affected = RemovedTrackDiff.AffectedFiles(rules, forLibrary);

        var scannedByPath = await LibraryScanStore.FilesAtPathsAsync(uow, libraryId, affected.Select(result => result.File.RelativePath)).ConfigureAwait(false);

        var items = affected.Select(result =>
        {
            var path = result.File.RelativePath;
            scannedByPath.TryGetValue(path, out var scanned);
            var titleName = scanned?.ManagerTitle ?? System.IO.Path.GetFileName(path);
            var canRedownload = ManagerRedownloadRules.CanRedownload(scanned?.ManagerKind, scanned?.ManagerConnectionId, scanned?.ManagerTitleId);
            return new PyDict()
                .Set("path", path)
                .Set("manager_kind", scanned?.ManagerKind)
                .Set("manager_title", scanned?.ManagerTitle)
                .Set("removed_tracks", new PyList(result.TracksNowWanted.Select(t => (PyJson)RemovedTrackOut(t))))
                .Set("can_redownload", canRedownload)
                .Set("confirmation_message", canRedownload ? ManagerRedownloadRules.DestructiveConfirmation(titleName, null) : null)
                .Set("unavailable_reason", canRedownload
                    ? null
                    : (scanned?.ManagerKind is { } kind && ManagerRedownloadRules.KindSupportsRedownload(kind)
                        ? "Weir does not know which manager file this is, so it cannot ask for a redownload automatically. Scan the library again."
                        : ManagerRedownloadRules.NoManagerMessage));
        }).ToList();

        return ApiRoutes.Ok(new PyDict()
            .Set("library_id", libraryId)
            .Set("titles", new PyList(items.Select(i => (PyJson)i)))
            .Set("total", items.Count));
    }

    /// <summary>
    /// #509 step 3: asks a manager to redownload one title's file. Requires <c>confirm_destructive</c>
    /// (the operator has seen <see cref="ManagerRedownloadRules.DestructiveConfirmation"/>'s exact wording — the
    /// web only shows this action once <c>GET .../library-redownloads</c> said <c>can_redownload: true</c>).
    /// Resolves the manager connection and title id #551's title matching stored on the latest scan for this
    /// path; answers <see cref="RedownloadOutcome.Unsupported"/> when no scan ever matched it (stale scan, or the
    /// file was cleaned by hand between the two), and <c>"failed"</c> when the manager call itself throws before
    /// committing to anything destructive (a network problem, not a data-safety one).
    /// </summary>
    private static async Task<ApiResult> PostRedownloadAsync(ApiRequest request)
    {
        var payload = await request.ReadBodyAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var libraryId = request.PathInt("library_id", issues);
        var model = new BodyModel(payload, issues);
        var path = model.Str("path", minLength: 1);
        var confirmedDestructive = model.Bool("confirm_destructive", false, required: true);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        request.RequireConfirmationToken(csrfToken);

        if (!confirmedDestructive)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "This action deletes the current file before asking for a new one; confirm_destructive must be true.");
        }

        var uow = await request.DbAsync().ConfigureAwait(false);
        var library = await RequireLibraryAsync(uow, libraryId, LibraryModeMapping.NoLibraryWithThatId).ConfigureAwait(false);
        var scanned = (await LibraryScanStore.FilesAtPathsAsync(uow, libraryId, [path!]).ConfigureAwait(false)).GetValueOrDefault(path!);

        if (!ManagerRedownloadRules.CanRedownload(scanned?.ManagerKind, scanned?.ManagerConnectionId, scanned?.ManagerTitleId))
        {
            await request.CommitAsync().ConfigureAwait(false);
            return ApiRoutes.Ok(new PyDict().Set("path", path).Set("outcome", "unsupported").Set("message", ManagerRedownloadRules.NoManagerMessage));
        }

        var connections = await request.Service<MediaManagerConnectionService>().ConnectionsByIdAsync(uow, [scanned!.ManagerConnectionId!.Value]).ConfigureAwait(false);
        var connection = connections.FirstOrDefault();
        await request.CommitAsync().ConfigureAwait(false);
        if (connection is null)
        {
            return ApiRoutes.Ok(new PyDict().Set("path", path).Set("outcome", "unsupported").Set("message", ManagerRedownloadRules.NoManagerMessage));
        }

        RedownloadResult result;
        try
        {
            result = await request.Service<IManagerRedownload>()
                .RequestRedownloadAsync(connection, library.MediaType, scanned.ManagerTitleId!, path, request.Context.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is MediaManagerHttpException or MediaManagerUnreachableException)
        {
            return ApiRoutes.Ok(new PyDict()
                .Set("path", path)
                .Set("outcome", "failed")
                .Set("message", $"Weir could not ask {connection.Label} to download this again: {exception.Message}"));
        }

        var outcome = result.Outcome switch
        {
            RedownloadOutcome.Requested => "requested",
            RedownloadOutcome.DeletedButSearchFailed => "deleted_but_search_failed",
            _ => "unsupported",
        };
        return ApiRoutes.Ok(new PyDict().Set("path", path).Set("outcome", outcome).Set("message", result.Summary));
    }
}
