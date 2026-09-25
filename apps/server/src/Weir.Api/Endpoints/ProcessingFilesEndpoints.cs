using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Weir.Api.Http;
using Weir.Core.Auth;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Core.Processing.RemuxPass;
using Weir.Core.Time;
using Weir.Core.Validation;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Processing.DirectPlay;
using Weir.Infrastructure.Sqlite;

namespace Weir.Api.Endpoints;

/// <summary>
/// The Files screen — <c>/api/v1/processing/files</c>. <c>passed_through</c> and <c>rejected</c> are valid
/// statuses in both the response and the <c>file_status</c> filter (#530). Covers the file list, delete, move-to-
/// top and requeue (one file or many). The per-file log lives in <see cref="ProcessingFileLogEndpoints"/>; the
/// #501 "choose tracks by hand" flow lives in <see cref="ProcessingFileTracksEndpoints"/>.
/// </summary>
public static class ProcessingFilesEndpoints
{
    public static IEndpointRouteBuilder MapProcessingFilesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var handlers = endpoints.ServiceProvider.GetRequiredService<ProcessingFilesEndpointHandlers>();
        endpoints.MapV1("GET", "/processing/files", handlers.GetFilesAsync);
        endpoints.MapV1("DELETE", "/processing/files/{file_id}", handlers.DeleteFileAsync);
        endpoints.MapV1("POST", "/processing/files/{file_id}/move-to-top", handlers.MoveToTopAsync);
        endpoints.MapV1("POST", "/processing/files/{file_id}/requeue", handlers.RequeueOneAsync);
        endpoints.MapV1("POST", "/processing/files/requeue", handlers.RequeueManyAsync);
        return endpoints;
    }

    /// <summary>Shared with <see cref="ProcessingFileLogEndpoints"/> and <see cref="ProcessingFileTracksEndpoints"/>.</summary>
    internal static async Task<ProcessingFileRecord> RequireFileAsync(UnitOfWork uow, FileStateStore files, long id) =>
        await files.GetAsync(uow, id).ConfigureAwait(false)
        ?? throw new ApiException(StatusCodes.Status404NotFound, "Weir has no record of that file.");
}

/// <summary>Handlers for <see cref="ProcessingFilesEndpoints"/>, constructor-injected with the stores they need.</summary>
internal sealed class ProcessingFilesEndpointHandlers
{
    /// <summary>The most files one bulk requeue takes, matching the most one list request returns.</summary>
    private const int BulkRequeueMaxFiles = 1000;

    private readonly FileStateStore _files;
    private readonly DirectPlayService _directPlay;
    private readonly LiveProgressStore _liveProgress;
    private readonly HandbackStore _handback;
    private readonly ProcessingJobStore _jobs;
    private readonly LibraryStore _libraries;

    public ProcessingFilesEndpointHandlers(
        FileStateStore files,
        DirectPlayService directPlay,
        LiveProgressStore liveProgress,
        HandbackStore handback,
        ProcessingJobStore jobs,
        LibraryStore libraries)
    {
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _directPlay = directPlay ?? throw new ArgumentNullException(nameof(directPlay));
        _liveProgress = liveProgress ?? throw new ArgumentNullException(nameof(liveProgress));
        _handback = handback ?? throw new ArgumentNullException(nameof(handback));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _libraries = libraries ?? throw new ArgumentNullException(nameof(libraries));
    }

    private static WireObject FileOut(ProcessingFileRecord row, string libraryName, List<DirectPlayBadge> directPlay, LiveProgress? progress)
    {
        var quarantined = row.Status == ProcessingFileStatuses.OnHold && row.FailureAttempts >= RetryPolicy.QuarantineAfterFailures;
        return new WireObject()
            .Set("kind", HistoryEntryKinds.Download)
            .Set("id", row.Id)
            .Set("library_id", row.LibraryId)
            .Set("library_name", libraryName)
            .Set("relative_path", row.RelativePath)
            .Set("status", row.Status)
            .Set("status_reason", row.StatusReason)
            .Set("blocked_by_connection", row.BlockedByConnection)
            .Set("size_bytes", row.SizeBytes)
            .Set("video_codec", row.VideoCodec)
            .Set("video_width", row.VideoWidth)
            .Set("video_height", row.VideoHeight)
            .Set("audio_track_count", row.AudioTrackCount)
            .Set("subtitle_track_count", row.SubtitleTrackCount)
            .Set("duration_seconds", row.DurationSeconds is { } d ? WireValue.Of(d) : WireValue.Null)
            .Set("direct_play", new WireArray(directPlay.Select(v => (WireValue)new WireObject()
                .Set("device_id", v.DeviceId)
                .Set("device_name", v.DeviceName)
                .Set("verdict", v.Verdict)
                .Set("reasons", new WireArray(v.Reasons.Select(r => (WireValue)WireValue.Of(r)))))))
            .Set("progress_percent", progress?.Percent is { } pct ? WireValue.Of(pct) : WireValue.Null)
            .Set("progress_message", progress?.Message)
            .Set("progress_eta_seconds", progress?.EtaSeconds is { } eta ? WireValue.Of(eta) : WireValue.Null)
            // What Live shows on a working file (docs/archive/live-and-library.md): which step, how fast,
            // and what is coming out. All null when nothing is running on the file.
            .Set("progress_status", progress?.Status)
            .Set("progress_speed", progress?.Speed)
            .Set("progress_elapsed_seconds", progress?.ElapsedSeconds is { } elapsed ? WireValue.Of(elapsed) : WireValue.Null)
            .Set("progress_removed_audio", progress is null ? WireValue.Null : new WireArray(progress.RemovedAudio.Select(t => (WireValue)WireValue.Of(t))))
            .Set("progress_removed_subtitles", progress is null ? WireValue.Null : new WireArray(progress.RemovedSubtitles.Select(t => (WireValue)WireValue.Of(t))))
            .Set("failure_class", row.FailureClass)
            .Set("failure_attempts", row.FailureAttempts)
            .Set("quarantined", quarantined)
            .Set("next_retry_at", row.NextRetryAt?.ToWireText())
            .Set("output_collision_policy", row.OutputCollisionPolicy)
            .Set("output_collision_action", row.OutputCollisionAction)
            .Set("output_collision_reason", row.OutputCollisionReason)
            .Set("hold_until", row.HoldUntil?.ToWireText())
            .Set("size_changed_at", row.SizeChangedAt?.ToWireText())
            .Set("created_at", row.CreatedAt.ToWireText())
            .Set("updated_at", row.UpdatedAt.ToWireText())
            .Set("last_seen_at", row.LastSeenAt?.ToWireText())
            .Set("last_attempt_at", row.LastAttemptAt?.ToWireText());
    }

    public async Task<ApiResult> GetFilesAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        long? libraryId = request.Query("library_id") is { } rawLibrary && FieldRules.TryInt(new WireString(rawLibrary), ["query", "library_id"], 1, null, issues, out var parsedLibrary)
            ? (long)parsedLibrary
            : null;
        var fileStatuses = ParseFileStatuses(request.Query("file_status"), issues);
        var pathContains = request.Query("path_contains");
        long? withinDays = request.Query("within_days") is { } rawWithin && FieldRules.TryInt(new WireString(rawWithin), ["query", "within_days"], 1, 3650, issues, out var parsedWithin)
            ? (long)parsedWithin
            : null;
        var limit = request.Query("limit") is { } rawLimit && FieldRules.TryInt(new WireString(rawLimit), ["query", "limit"], 1, 1000, issues, out var parsedLimit) ? (int)parsedLimit : 200;
        issues.ThrowIfAny();

        var uow = await request.DbAsync().ConfigureAwait(false);
        var filter = new ProcessingFileListFilter
        {
            LibraryId = libraryId,
            Statuses = fileStatuses,
            PathContains = pathContains,
            Since = withinDays is { } days ? Timestamp.FromUtc(request.Time.GetUtcNow().AddDays(-days).UtcDateTime) : null,
            Limit = limit,
        };
        var rows = await _files.ListAsync(uow, filter).ConfigureAwait(false);
        var libraryNames = await _files.LibraryNamesAsync(uow).ConfigureAwait(false);
        var knownDevices = DeviceProfileLoader.Load(request.Options.WeirHome);
        var devices = await _directPlay.SelectedProfilesAsync(uow, knownDevices).ConfigureAwait(false);
        var progressByPath = _liveProgress.Snapshot();
        var handbacks = await _handback.ForLibrariesAsync(uow, rows.Select(row => row.LibraryId)).ConfigureAwait(false);

        var files = new List<WireValue>();
        foreach (var row in rows)
        {
            var libraryName = libraryNames.GetValueOrDefault(row.LibraryId, "Unknown library");
            var directPlay = DirectPlayService.ForRow(row, devices);
            progressByPath.TryGetValue(row.RelativePath, out var progress);
            // #652: the copy Weir handed back, and what a media manager said about it, for History.
            handbacks.TryGetValue((row.LibraryId, row.RelativePath), out var handback);
            files.Add(FileOut(row, libraryName, directPlay, progress).Set("handback", HandbackStore.ToOut(handback)));
        }

        var counts = await _files.StatusCountsAsync(uow, libraryId).ConfigureAwait(false);
        return ApiRoutes.Ok(new WireObject()
            .Set("files", new WireArray(files))
            .Set("status_counts", new WireObject().Also(dict =>
            {
                foreach (var (status, count) in counts)
                {
                    dict.Set(status, count);
                }
            }))
            .Set("returned", files.Count)
            .Set("limit", limit));
    }

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

    public async Task<ApiResult> DeleteFileAsync(ApiRequest request)
    {
        var payload = await request.ReadBodyAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var id = request.PathInt("file_id", issues);
        var model = new BodyModel(payload, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        request.RequireConfirmationToken(csrfToken);

        var uow = await request.DbAsync().ConfigureAwait(false);
        await ProcessingFilesEndpoints.RequireFileAsync(uow, _files, id).ConfigureAwait(false);
        await _files.ForgetAsync(uow, id).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        return new CustomApiResult(context =>
        {
            ApiResponses.NoContentJson(context);
            return Task.CompletedTask;
        });
    }

    public async Task<ApiResult> MoveToTopAsync(ApiRequest request)
    {
        var payload = await request.ReadBodyAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var id = request.PathInt("file_id", issues);
        var model = new BodyModel(payload, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        request.RequireConfirmationToken(csrfToken);

        var uow = await request.DbAsync().ConfigureAwait(false);
        var row = await ProcessingFilesEndpoints.RequireFileAsync(uow, _files, id).ConfigureAwait(false);
        var job = await FindPendingRemuxJobAsync(uow, row.RelativePath).ConfigureAwait(false);
        if (job is null)
        {
            await request.CommitAsync().ConfigureAwait(false);
            return ApiRoutes.Ok(new WireObject()
                .Set("moved", false)
                .Set("detail", "There is no queued work for this file to move. It may already be running, or it may not have been picked up by a scan yet."));
        }

        // Commit before MoveToTopAsync, or the request deadlocks against the store's own connection:
        // RequireUserAsync may have refreshed user_sessions.last_seen_at, and that write holds SQLite's single
        // write lock until this commit runs, while the store's BEGIN IMMEDIATE waits for it. Everything above is
        // a read apart from that session touch, so committing here changes nothing else.
        await request.CommitAsync().ConfigureAwait(false);
        var outcome = await _jobs.MoveToTopAsync(job.Value.Id).ConfigureAwait(false);
        if (outcome != JobActionOutcome.Ok)
        {
            return ApiRoutes.Ok(new WireObject().Set("moved", false).Set("detail", "This file's work has already started, so it cannot be moved ahead of anything."));
        }

        return ApiRoutes.Ok(new WireObject().Set("moved", true).Set("detail", "Moved to the front of the queue. It starts as soon as there is capacity for it."));
    }

    /// <summary>The oldest pending remux job for this path.</summary>
    private static async Task<(long Id, string PayloadJson)?> FindPendingRemuxJobAsync(UnitOfWork uow, string relativePath)
    {
        var rows = await uow.QueryAsync(
            "SELECT id, payload_json FROM jobs WHERE job_kind = @kind AND status = 'pending' ORDER BY id",
            reader => (Id: reader.GetInt64(0), Payload: reader.IsDBNull(1) ? null : reader.GetString(1)),
            ("@kind", RequeueStore.RemuxPassJobKind)).ConfigureAwait(false);
        foreach (var (id, payloadJson) in rows)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                continue;
            }

            WireValue parsed;
            try
            {
                parsed = WireJsonParser.Parse(payloadJson);
            }
            catch (WireJsonDecodeException)
            {
                continue;
            }

            if (parsed is WireObject dict && dict.TryGetValue("relative_media_path", out var value) && value is WireString s && s.Value == relativePath)
            {
                return (id, payloadJson);
            }
        }

        return null;
    }

    public async Task<ApiResult> RequeueOneAsync(ApiRequest request)
    {
        var payload = await request.ReadBodyAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var id = request.PathInt("file_id", issues);
        var model = new BodyModel(payload, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        request.RequireConfirmationToken(csrfToken);

        var uow = await request.DbAsync().ConfigureAwait(false);
        var row = await ProcessingFilesEndpoints.RequireFileAsync(uow, _files, id).ConfigureAwait(false);
        var result = await new RequeueStore(_jobs, _libraries).RequeueFileAsync(uow, row).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(new WireObject().Set("requeued", result.Requeued).Set("skipped", result.Skipped).Set("detail", result.Detail));
    }

    public async Task<ApiResult> RequeueManyAsync(ApiRequest request)
    {
        var payload = await request.ReadBodyAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var model = new BodyModel(payload, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        var libraryId = model.OptionalInt("library_id", ge: 1);
        string? fileStatus = null;
        var rawStatus = model.OptionalStr("file_status");
        if (rawStatus is not null && FieldRules.TryLiteral(WireValue.Of(rawStatus), ["body", "file_status"], ProcessingFileStatuses.All, issues, out var parsedStatus))
        {
            fileStatus = parsedStatus;
        }

        var pathContains = model.OptionalStr("path_contains", maxLength: 500);
        var limit = model.Number("limit", 200, required: false, ge: 1, le: BulkRequeueMaxFiles);
        var fileIds = model.IntList("file_ids");
        if (fileIds.Count > BulkRequeueMaxFiles)
        {
            issues.Add(new ValidationIssue(
                "too_long",
                ["body", "file_ids"],
                $"List should have at most {BulkRequeueMaxFiles} items after validation, not {fileIds.Count}",
                WireValue.Null,
                new WireObject().Set("field_type", "List").Set("max_length", BulkRequeueMaxFiles).Set("actual_length", fileIds.Count)));
        }

        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        request.RequireConfirmationToken(csrfToken);

        var uow = await request.DbAsync().ConfigureAwait(false);
        // file_ids is the exact set a person chose on screen, so the other filters still narrow it but the limit
        // never cuts it short.
        var rows = await _files.ListAsync(uow, new ProcessingFileListFilter
        {
            LibraryId = libraryId,
            Statuses = fileStatus is not null ? [fileStatus] : null,
            PathContains = pathContains,
            Ids = fileIds.Count > 0 ? fileIds : null,
            Limit = fileIds.Count > 0 ? BulkRequeueMaxFiles : (int)limit,
        }).ConfigureAwait(false);
        var result = await new RequeueStore(_jobs, _libraries).RequeueFilesAsync(uow, rows).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(new WireObject().Set("requeued", result.Requeued).Set("skipped", result.Skipped).Set("detail", result.Detail));
    }
}

file static class WireObjectExtensions
{
    public static WireObject Also(this WireObject dict, Action<WireObject> configure)
    {
        configure(dict);
        return dict;
    }
}
