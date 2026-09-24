using Microsoft.AspNetCore.Routing;
using Weir.Api.Http;
using Weir.Core.Json;
using Weir.Core.LibraryMode;
using Weir.Core.Processing;
using Weir.Core.Time;
using Weir.Core.Validation;
using Weir.Infrastructure.LibraryMode;
using Weir.Infrastructure.Sqlite;
using static Weir.Api.Endpoints.EndpointLookups;

namespace Weir.Api.Endpoints;

/// <summary>
/// Library mode (#505, #568): the Overview and Problems dashboards for a library — what it holds, how much the
/// rules would touch, the breakdowns by codec/resolution/language, and every reason a file is not something Weir
/// will clean. See <see cref="LibraryModeEndpoints"/> for the rest of the Library mode surface.
/// </summary>
public static class LibraryModeOverviewEndpoints
{
    public static IEndpointRouteBuilder MapLibraryModeOverviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapV1("GET", "/processing/libraries/{library_id}/library-overview", GetOverviewAsync);
        endpoints.MapV1("GET", "/processing/libraries/{library_id}/library-problems", GetProblemsAsync);
        return endpoints;
    }

    /// <summary>
    /// "Scheduled scan and clean": whether it is on and when it next runs. <c>next_run_at</c> is null when it is off or
    /// cannot run (no library folders, the library switched off, a window that never opens); a run that is due is
    /// reported as now, since the timer starts it within half a minute.
    /// </summary>
    private static async Task<WireObject> ScheduleOutAsync(
        UnitOfWork uow, ProcessingLibraryRecord library, LibrarySettings settings, DateTimeOffset now)
    {
        var next = await LibraryModeScheduling.NextRunAsync(uow, library, settings, now).ConfigureAwait(false);
        return new WireObject()
            .Set("enabled", settings.ScheduleEnabled)
            .Set("next_run_at", next is { } at ? Timestamp.FromDateTimeOffset(at < now ? now : at).ToWireText() : null);
    }

    /// <summary><c>share</c> is computed here, not in the browser, so every bar in the UI is drawn from one number.</summary>
    private static WireObject BreakdownRowOut(LibraryBreakdownRow row, long totalFiles) => new WireObject()
        .Set("value", row.Value)
        .Set("files", row.Files)
        .Set("size_bytes", row.SizeBytes)
        .Set("share", totalFiles > 0 ? Math.Round((double)row.Files / totalFiles, 4) : 0.0);

    private static WireObject ProblemGroupOut(LibraryProblemGroup group) => new WireObject()
        .Set("kind", LibraryProblems.Name(group.Kind))
        .Set("title", LibraryProblems.Title(group.Kind))
        .Set("what_to_do", LibraryProblems.WhatToDo(group.Kind))
        .Set("files", group.Files)
        .Set("size_bytes", group.SizeBytes)
        .Set("sample_paths", new WireArray(group.SampleFiles.Select(path => (WireValue)new WireString(path))));

    /// <summary>
    /// #568's Overview: what the library holds and how much of it the rules would touch, plus the breakdowns by
    /// codec, resolution class and language. Every number is a SQL aggregate over <c>library_files</c> and its
    /// facet rows (issue #568 point 7) — this never sends thousands of file rows for the browser to add up.
    /// </summary>
    private static async Task<ApiResult> GetOverviewAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var libraryId = request.PathInt("library_id", issues);
        issues.ThrowIfAny();

        var uow = await request.DbAsync().ConfigureAwait(false);
        var library = await RequireLibraryAsync(uow, libraryId, LibraryModeMapping.NoLibraryWithThatId).ConfigureAwait(false);
        var settings = await LibrarySettingsStore.GetAsync(uow, libraryId).ConfigureAwait(false);
        var totals = await LibraryViewStore.TotalsAsync(uow, libraryId).ConfigureAwait(false);
        var breakdowns = await LibraryViewStore.AllBreakdownsAsync(uow, libraryId).ConfigureAwait(false);
        var problems = await LibraryViewStore.ProblemsAsync(uow, libraryId, settings.CleanHardlinkedFiles).ConfigureAwait(false);

        var breakdownsOut = new WireObject();
        foreach (var facet in LibraryFacets.All)
        {
            var rows = breakdowns.GetValueOrDefault(facet, []);
            breakdownsOut.Set(facet, new WireArray(rows.Select(row => (WireValue)BreakdownRowOut(row, totals.Files))));
        }

        return ApiRoutes.Ok(new WireObject()
            .Set("library_id", libraryId)
            .Set("folders_configured", settings.Folders.Count)
            .Set("scan", await LibraryModeMapping.ScanOutAsync(uow, libraryId, LibraryModeMapping.Logger(request)).ConfigureAwait(false))
            .Set("schedule", await ScheduleOutAsync(uow, library, settings, request.Time.GetUtcNow()).ConfigureAwait(false))
            .Set("totals", LibraryModeMapping.TotalsOut(totals))
            .Set("breakdowns", breakdownsOut)
            .Set("problems", new WireArray(problems.Select(group => (WireValue)ProblemGroupOut(group)))));
    }

    /// <summary>#568's Problems view: every reason a file is not something Weir will clean, grouped, with advice.</summary>
    private static async Task<ApiResult> GetProblemsAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var libraryId = request.PathInt("library_id", issues);
        issues.ThrowIfAny();

        var uow = await request.DbAsync().ConfigureAwait(false);
        await RequireLibraryAsync(uow, libraryId, LibraryModeMapping.NoLibraryWithThatId).ConfigureAwait(false);
        var settings = await LibrarySettingsStore.GetAsync(uow, libraryId).ConfigureAwait(false);
        var groups = await LibraryViewStore.ProblemsAsync(uow, libraryId, settings.CleanHardlinkedFiles).ConfigureAwait(false);
        return ApiRoutes.Ok(new WireObject()
            .Set("library_id", libraryId)
            .Set("scan", await LibraryModeMapping.ScanOutAsync(uow, libraryId, LibraryModeMapping.Logger(request)).ConfigureAwait(false))
            .Set("groups", new WireArray(groups.Select(group => (WireValue)ProblemGroupOut(group))))
            .Set("total", groups.Sum(group => group.Files)));
    }
}
