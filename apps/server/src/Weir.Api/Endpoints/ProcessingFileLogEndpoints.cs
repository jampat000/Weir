using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Weir.Api.Http;
using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Core.Validation;
using Weir.Infrastructure.Processing;

namespace Weir.Api.Endpoints;

/// <summary>
/// The per-file processing log — <c>/api/v1/processing/files/{file_id}/log</c> (view and plain-text download). See
/// <see cref="ProcessingFilesEndpoints"/> for the rest of the Files screen.
/// </summary>
public static class ProcessingFileLogEndpoints
{
    public static IEndpointRouteBuilder MapProcessingFileLogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapV1("GET", "/processing/files/{file_id}/log", GetFileLogAsync);
        endpoints.MapV1("GET", "/processing/files/{file_id}/log/download", DownloadFileLogAsync);
        return endpoints;
    }

    private static async Task<ApiResult> GetFileLogAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var id = request.PathInt("file_id", issues);
        var limit = request.Query("limit") is { } rawLimit && FieldRules.TryInt(new WireString(rawLimit), ["query", "limit"], 1, 500, issues, out var parsedLimit) ? (int)parsedLimit : 50;
        issues.ThrowIfAny();

        var uow = await request.DbAsync().ConfigureAwait(false);
        var row = await ProcessingFilesEndpoints.RequireFileAsync(uow, id).ConfigureAwait(false);
        var operatorRow = await OperatorSettingsStore.EnsureAsync(uow).ConfigureAwait(false);
        var rows = await FileLogStore.LogsForFileAsync(uow, row.RelativePath, limit).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);

        var entries = new List<WireValue>();
        foreach (var entry in rows)
        {
            var detail = FileLogStore.ParseDetail(entry.DetailJson);
            var story = FileStory.NarratePass(detail, entry.LibraryName);
            entries.Add(new WireObject()
                .Set("id", entry.Id)
                .Set("recorded_at", entry.RecordedAt.ToWireText())
                .Set("outcome", entry.Outcome)
                .Set("title", entry.Title)
                .Set("library_name", entry.LibraryName)
                .Set("detail", detail)
                .Set("story", new WireArray(story.Select(step => (WireValue)new WireObject()
                    .Set("heading", step.Heading)
                    .Set("sentence", step.Sentence)
                    .Set("tone", step.Tone.ToString().ToLowerInvariant())))));
        }

        return ApiRoutes.Ok(new WireObject()
            .Set("file_id", id)
            .Set("relative_path", row.RelativePath)
            .Set("retention_days", operatorRow.FileLogRetentionDays)
            .Set("entries", new WireArray(entries)));
    }

    private static async Task<ApiResult> DownloadFileLogAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var id = request.PathInt("file_id", issues);
        issues.ThrowIfAny();

        var uow = await request.DbAsync().ConfigureAwait(false);
        var row = await ProcessingFilesEndpoints.RequireFileAsync(uow, id).ConfigureAwait(false);
        var rows = await FileLogStore.LogsForFileAsync(uow, row.RelativePath, 500).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);

        var safe = new string([.. row.RelativePath.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')]);
        safe = safe.Length > 80 ? safe[^80..] : safe;
        safe = safe.Trim('-');
        if (safe.Length == 0)
        {
            safe = "file";
        }

        var text = FileLogStore.RenderLogText(rows);
        return new CustomApiResult(context =>
        {
            context.Response.Headers.ContentDisposition = $"attachment; filename=\"weir-{safe}.log.txt\"";
            return ApiResponses.WritePlainTextAsync(context, StatusCodes.Status200OK, text);
        });
    }
}
