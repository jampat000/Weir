using Microsoft.AspNetCore.Routing;
using Weir.Api.Http;
using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Core.Time;
using Weir.Core.Validation;
using Weir.Infrastructure.LibraryMode;
using Weir.Infrastructure.Processing;

namespace Weir.Api.Endpoints;

/// <summary>
/// History's library cleans, <c>/api/v1/processing/library-cleans</c> (#695): what the newest clean did to each library
/// file, filtered the way <c>/processing/files</c> filters downloads, so History lists both kinds side by side.
/// </summary>
public static class ProcessingLibraryCleansEndpoints
{
    private const int PathContainsMaxLength = 400;
    private const int WithinDaysMax = 3650;

    public static IEndpointRouteBuilder MapProcessingLibraryCleansEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapV1("GET", "/processing/library-cleans", GetLibraryCleansAsync);
        return endpoints;
    }

    private static async Task<ApiResult> GetLibraryCleansAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        long? libraryId = request.Query("library_id") is { } rawLibrary && PydanticRules.TryInt(new PyStr(rawLibrary), ["query", "library_id"], 1, null, issues, out var parsedLibrary)
            ? (long)parsedLibrary
            : null;
        string? pathContains = request.Query("path_contains") is { } rawPath && PydanticRules.TryStr(new PyStr(rawPath), ["query", "path_contains"], null, PathContainsMaxLength, issues, out var parsedPath)
            ? parsedPath
            : null;
        long? withinDays = request.Query("within_days") is { } rawWithin && PydanticRules.TryInt(new PyStr(rawWithin), ["query", "within_days"], 1, WithinDaysMax, issues, out var parsedWithin)
            ? (long)parsedWithin
            : null;
        var limit = request.Query("limit") is { } rawLimit && PydanticRules.TryInt(new PyStr(rawLimit), ["query", "limit"], 1, LibraryCleanHistoryFilter.MaxLimit, issues, out var parsedLimit)
            ? (int)parsedLimit
            : LibraryCleanHistoryFilter.DefaultLimit;
        issues.ThrowIfAny();

        var uow = await request.DbAsync().ConfigureAwait(false);
        var rows = await LibraryCleanHistoryStore.ListAsync(uow, new LibraryCleanHistoryFilter
        {
            LibraryId = libraryId,
            PathContains = pathContains,
            Since = withinDays is { } days ? PyDateTime.FromUtc(request.Time.GetUtcNow().AddDays(-days).UtcDateTime) : null,
            Limit = limit,
        }).ConfigureAwait(false);
        var libraryNames = await FileStateStore.LibraryNamesAsync(uow).ConfigureAwait(false);

        var cleans = rows.Select(row => (PyJson)new PyDict()
            .Set("kind", HistoryEntryKinds.LibraryClean)
            .Set("id", row.Id)
            .Set("library_id", row.LibraryId is { } id ? PyJson.Of(id) : PyJson.Null)
            .Set("library_name", row.LibraryId is { } known ? libraryNames.GetValueOrDefault(known, "Unknown library") : "Unknown library")
            .Set("relative_path", row.RelativePath)
            .Set("outcome", row.Outcome)
            .Set("detail", row.Detail)
            .Set("trigger", row.Trigger)
            .Set("recorded_at", row.RecordedAt.PydanticJson()));
        return ApiRoutes.Ok(new PyDict()
            .Set("cleans", new PyList(cleans))
            .Set("returned", rows.Count)
            .Set("limit", limit));
    }
}
