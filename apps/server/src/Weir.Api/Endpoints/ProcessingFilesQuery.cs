using Weir.Api.Http;
using Weir.Core.Json;
using Weir.Core.Paging;
using Weir.Core.Processing;
using Weir.Core.Time;
using Weir.Core.Validation;
using Weir.Infrastructure.Processing;

namespace Weir.Api.Endpoints;

/// <summary>What a request for <c>GET /api/v1/processing/files</c> asks for, read from its query string.</summary>
internal static class ProcessingFilesQuery
{
    private const int MaxWithinDays = 3650;
    private const int MaxLimit = 1000;
    private const int DefaultLimit = 200;

    /// <summary>The query string as a filter. Every problem is reported at once, as the other lists do.</summary>
    public static ProcessingFileListFilter Read(ApiRequest request, ValidationIssues issues)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(issues);
        long? libraryId = request.Query("library_id") is { } rawLibrary && FieldRules.TryInt(new WireString(rawLibrary), ["query", "library_id"], 1, null, issues, out var parsedLibrary)
            ? (long)parsedLibrary
            : null;
        var fileStatuses = ParseFileStatuses(request.Query("file_status"), issues);
        var pathContains = request.Query("path_contains");
        long? withinDays = request.Query("within_days") is { } rawWithin && FieldRules.TryInt(new WireString(rawWithin), ["query", "within_days"], 1, MaxWithinDays, issues, out var parsedWithin)
            ? (long)parsedWithin
            : null;
        var limit = request.Query("limit") is { } rawLimit && FieldRules.TryInt(new WireString(rawLimit), ["query", "limit"], 1, MaxLimit, issues, out var parsedLimit) ? (int)parsedLimit : DefaultLimit;
        var order = ReadOrder(request, issues);
        return new ProcessingFileListFilter
        {
            LibraryId = libraryId,
            Statuses = fileStatuses,
            PathContains = pathContains,
            Since = withinDays is { } days ? Timestamp.FromUtc(request.Time.GetUtcNow().AddDays(-days).UtcDateTime) : null,
            Limit = limit,
            Sort = order.Sort,
            Direction = order.Direction,
            After = order.After,
        };
    }

    /// <summary>The order the list was asked for. A request that names no sort gets the order the list has always had.</summary>
    private static ProcessingFileOrder ReadOrder(ApiRequest request, ValidationIssues issues)
    {
        var sort = ProcessingFileSort.LastSeen;
        var sortValid = request.Query("sort") is not { } rawSort
            || (FieldRules.TryLiteral(new WireString(rawSort), ["query", "sort"], ProcessingFileSorts.All, issues, out var parsedSort)
                && ProcessingFileSorts.TryParse(parsedSort, out sort));
        var direction = SortDirection.Descending;
        var directionValid = request.Query("direction") is not { } rawDirection
            || (FieldRules.TryLiteral(new WireString(rawDirection), ["query", "direction"], SortDirections.All, issues, out var parsedDirection)
                && SortDirections.TryParse(parsedDirection, out direction));
        if (!sortValid || !directionValid || request.Query("cursor") is not { Length: > 0 } cursor)
        {
            return new ProcessingFileOrder(sort, direction, null);
        }

        if (ProcessingFileOrdering.TryDecodeCursor(cursor, sort, direction, out var after))
        {
            return new ProcessingFileOrder(sort, direction, after);
        }

        issues.Add(new ValidationIssue("value_error", ["query", "cursor"], "Value error, that is not a position this list gave out for this sort", new WireString(cursor)));
        return new ProcessingFileOrder(sort, direction, null);
    }

    private sealed record ProcessingFileOrder(ProcessingFileSort Sort, SortDirection Direction, IReadOnlyList<object?>? After);

    /// <summary>
    /// <c>file_status</c> as one or more comma-separated statuses (#781): the Processing screen asks for every
    /// currently-processing file in one uncapped, status-filtered page, separate from the ordinary paginated
    /// list, so a running file can never be pushed off by the page limit. Null when the query omits the
    /// parameter; an empty list (a blank value, or one made entirely of blanks) filters nothing, same as omitting it.
    /// </summary>
    private static List<string>? ParseFileStatuses(string? rawStatuses, ValidationIssues issues)
    {
        if (rawStatuses is null)
        {
            return null;
        }

        var statuses = new List<string>();
        foreach (var token in rawStatuses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (FieldRules.TryLiteral(new WireString(token), ["query", "file_status"], ProcessingFileStatuses.All, issues, out var parsed))
            {
                statuses.Add(parsed);
            }
        }

        return statuses;
    }
}
