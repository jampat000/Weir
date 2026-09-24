using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Weir.Api.Http;
using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Validation;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Api.Endpoints;

/// <summary>
/// The intake webhook and hand-off status: a media manager's import events arrive here, and Weir's own
/// hand-back status and outcome routes let it report what became of a file Weir handed back.
/// </summary>
public static class MediaManagerIntakeEndpoints
{
    public static IEndpointRouteBuilder MapMediaManagerIntakeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapV1("POST", "/intake/webhook/{source_key}", PostWebhookAsync);
        endpoints.MapV1("GET", "/intake/capabilities", GetIntakeCapabilitiesAsync);
        endpoints.MapV1("GET", "/intake/library-folders", GetLibraryFoldersAsync);
        endpoints.MapV1("GET", "/intake/handoffs/{source_key}/{handoff_id}", GetHandoffAsync);
        endpoints.MapV1("DELETE", "/intake/handoffs/{source_key}/{handoff_id}", DeleteHandoffAsync);
        endpoints.MapV1("POST", "/intake/handoffs/{source_key}/{handoff_id}/outcome", PostHandoffOutcomeAsync);
        return endpoints;
    }

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
        WireObject payload = new();
        if (body is null or WireNull)
        {
            issues.Add(FieldRules.Missing(["body"], WireNull.Instance));
        }
        else
        {
            FieldRules.TryDict(body, ["body"], issues, out payload);
        }

        issues.ThrowIfAny();

        var dialect = ImportEvents.DialectForSource(sourceKey)
            ?? throw new ApiException(StatusCodes.Status404NotFound, IntakeRules.UnknownSourceDetail(sourceKey));
        var uow = await request.DbAsync().ConfigureAwait(false);
        var identity = await RefusalsAsApiErrors(() => Intake(request).AuthoriseAsync(uow, dialect.Key, presented)).ConfigureAwait(false);

        var importEvent = dialect.Normalize(payload);
        if (importEvent is null)
        {
            return ApiRoutes.Ok(new WireObject().Set("status", "ignored").Set("source", dialect.Key));
        }

        if (importEvent.EventKind == MediaManagerImportEvent.Imported)
        {
            // #652: Sonarr's and Radarr's "imported" is heard now. A file Weir handed back is recorded, and Weir's copy
            // released when that is safe; anything else is answered as before and changes nothing.
            var imported = await request.Service<HandbackOutcomes>()
                .RecordManagerImportAsync(uow, importEvent, ManagerName(dialect.Key), identity.Authenticated).ConfigureAwait(false);
            if (!imported.Matched)
            {
                return ApiRoutes.Ok(new WireObject().Set("status", "ignored").Set("source", dialect.Key).Set("event", importEvent.EventKind));
            }

            await request.CommitAsync().ConfigureAwait(false);
            return ApiRoutes.Ok(new WireObject()
                .Set("status", "ok")
                .Set("source", dialect.Key)
                .Set("event", importEvent.EventKind)
                .Set("matched", true)
                .Set("released", imported.Released)
                .Set("message", imported.Message));
        }

        var enqueued = await RefusalsAsApiErrors(() => Intake(request).EnqueueRefineAsync(uow, importEvent, identity.ConnectionId)).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(new WireObject()
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
        await RefusalsAsApiErrors(() => Intake(request).RequireSecretAsync(uow, presented, null)).ConfigureAwait(false);
        return ApiRoutes.Ok(new WireObject().Set("capabilities", new WireArray(IntakeRules.HandoffCapabilities.Select(c => (WireValue)new WireString(c)))));
    }

    /// <summary>
    /// <c>GET /intake/library-folders</c>: every enabled library's watched, work and output folders, so a
    /// media manager reads them instead of a person retyping them. Read only; authenticated like
    /// <see cref="GetIntakeCapabilitiesAsync"/>, with no source key of its own (any connection's secret, or the
    /// shared instance-wide secret, proves a caller may read it).
    /// </summary>
    private static async Task<ApiResult> GetLibraryFoldersAsync(ApiRequest request)
    {
        var presented = request.FirstHeader("X-Webhook-Secret");
        var uow = await request.DbAsync().ConfigureAwait(false);
        await RefusalsAsApiErrors(() => Intake(request).RequireSecretAsync(uow, presented, null)).ConfigureAwait(false);
        var rows = await LibraryStore.ListAsync(uow, enabledOnly: true).ConfigureAwait(false);
        var weirHome = request.Options.WeirHome;
        var libraries = rows
            .Select(row => new PublishedLibraryFolders(
                row.Id,
                row.Name,
                row.MediaType,
                row.WatchedFolder,
                ProcessingLibraryFolders.EffectiveWorkFolder(
                    new ProcessingLibraryFolderRow(row.Id, row.MediaType, (int)row.DisplayOrder, row.WorkFolder, row.OutputFolder), weirHome),
                row.OutputFolder))
            .ToList();
        return ApiRoutes.Ok(LibraryFolderPublishing.ToOut(libraries));
    }

    private static async Task<(UnitOfWork Uow, string Key, HandoffLedgerRow Row)> RequireHandoffAsync(ApiRequest request)
    {
        var sourceKey = request.RouteValue("source_key") ?? string.Empty;
        var handoffId = request.RouteValue("handoff_id") ?? string.Empty;
        var presented = request.FirstHeader("X-Webhook-Secret");
        var key = Source(sourceKey);
        var uow = await request.DbAsync().ConfigureAwait(false);
        var identity = await RefusalsAsApiErrors(() => Intake(request).RequireSecretAsync(uow, presented, key)).ConfigureAwait(false);
        var row = await HandoffLedgerStore.FindAsync(uow, key, handoffId).ConfigureAwait(false)
            ?? throw new ApiException(StatusCodes.Status404NotFound, IntakeRules.NeverReceivedDetail);

        // A secret that names a specific, different connection is refused; the shared instance-wide secret still
        // works for any hand-off whose own connection has no secret of its own, matching AuthoriseAsync's fallback.
        if (row.ConnectionId is { } owner && identity.ConnectionId is { } matched && matched != owner)
        {
            throw new ApiException(StatusCodes.Status401Unauthorized, IntakeRules.MissingSecretDetail);
        }

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
    /// hand-off never received is 404; one not finished, or with a different outcome already recorded, is 409 with a
    /// <c>code</c> saying which (#664); a body that cannot be read is 422. Authenticated by <c>X-Webhook-Secret</c>, like
    /// the other hand-off routes.
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
        if (body is WireObject dict)
        {
            if (!dict.TryGetValue("occurredUtc", out var raw))
            {
                issues.Add(FieldRules.Missing(["body", "occurredUtc"], dict));
            }
            else if (raw is WireNull)
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
                        : $"{manager} already said it will not import this file, so Weir kept that answer.")
                {
                    Code = HandbackRules.OutcomeAlreadyRecordedCode,
                };
            }

            return ApiRoutes.Ok(OutcomeOut(row.HandoffId, recorded, row.OutcomeReleased, row.OutcomeMessage ?? string.Empty));
        }

        var status = await Intake(request).Ledger.CurrentStatusAsync(uow, row).ConfigureAwait(false);
        if (status.State is not (HandoffLedgerRules.Completed or HandoffLedgerRules.PassedThrough))
        {
            await request.CommitAsync().ConfigureAwait(false);
            var ended = HandoffLedgerRules.TerminalStates.Contains(status.State);
            throw new ApiException(
                StatusCodes.Status409Conflict,
                ended
                    ? $"This hand-off ended {status.State}, so Weir handed back no file to import."
                    : $"Weir has not finished this hand-off yet (it is {status.State}), so there is no file to import.")
            {
                Code = ended ? HandbackRules.HandoffEndedCode : HandbackRules.HandoffNotFinishedCode,
            };
        }

        var result = await request.Service<HandbackOutcomes>().RecordHandoffOutcomeAsync(
            uow, row, manager, outcome, occurred!.Value.AsUtc, importedPath, reason).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(OutcomeOut(row.HandoffId, outcome, result.Released, result.Message));
    }

    private static WireObject OutcomeOut(string handoffId, string outcome, bool released, string message) => new WireObject()
        .Set("handoffId", handoffId)
        .Set("outcome", outcome)
        .Set("released", released)
        .Set("message", message);
}
