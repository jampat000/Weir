using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Core.Media;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>What History's remove dialog did (#785): whether it went through, and the sentence to show for it.</summary>
public sealed record FileRemovalOutcome(bool Done, string Message);

/// <summary>
/// The "delete" and "keep" choices on History's remove dialog for a failed or rejected title whose file is still in
/// the watched folder (#785). "Delete" reuses <see cref="RejectRoutes"/> — the same manager conversation the
/// automatic reject policy has — falling back to deleting the file itself, strictly through
/// <see cref="RemuxPassPaths.CleanupRejectedFile"/>, only when no linked manager can take it. "Keep" records a skip
/// marker (<see cref="FileSkipMarkerStore"/>) and, where the file came from a manager's hand-off, tells that manager
/// it will not be imported. Neither choice ever reports success without telling the file's row and Activity what
/// actually happened; a manager that refuses or does not answer fails the whole action.
/// </summary>
public sealed class HistoryFileRemovalService
{
    private const string DeleteReason = "A person chose to delete this download and ask for another copy.";
    private const string KeepReason = "A person chose to keep this file without processing it again.";

    private readonly IMediaManagerPorts _ports;
    private readonly MediaManagerConnectionService _connections;
    private readonly HandoffCompletionReporter _reporter;
    private readonly RejectRoutes _routes;
    private readonly LibraryStore _libraries;
    private readonly FileStateStore _files;
    private readonly FileSkipMarkerStore _skipMarkers;

    public HistoryFileRemovalService(
        IMediaManagerPorts ports,
        MediaManagerConnectionService connections,
        HandoffCompletionReporter reporter,
        RejectRoutes routes,
        LibraryStore libraries,
        FileStateStore files,
        FileSkipMarkerStore skipMarkers)
    {
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
        _libraries = libraries ?? throw new ArgumentNullException(nameof(libraries));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _skipMarkers = skipMarkers ?? throw new ArgumentNullException(nameof(skipMarkers));
    }

    /// <summary>Which manager (if any) "delete" would ask, or that Weir would delete the file itself, and whether "keep" has one to tell.</summary>
    private sealed record ManagerRoute(HandoffReportTarget? HandoffTarget, HandoffOrigin? Origin, IReadOnlyList<ManagerConnection> QueueConnections, string? Label, bool DeleteHandled, bool KeepNotifies)
    {
        public static readonly ManagerRoute None = new(null, null, [], null, false, false);
    }

    /// <summary>What the remove dialog should offer for this title, read before it is shown.</summary>
    public async Task<FileRemovalOptions> EvaluateAsync(UnitOfWork uow, ProcessingFileRecord file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(file);
        if (file.Status is not (ProcessingFileStatuses.ProcessingFailed or ProcessingFileStatuses.Rejected))
        {
            return FileRemovalOptions.PlainRemove;
        }

        var library = await RemuxPassHandler.ResolveLibraryAsync(uow, _libraries, file.LibraryId, null).ConfigureAwait(false);
        if (library is null || !SourceFileExists(library, file.RelativePath))
        {
            return FileRemovalOptions.PlainRemove;
        }

        var route = await ResolveManagerRouteAsync(uow, library, file.RelativePath, cancellationToken).ConfigureAwait(false);
        return new FileRemovalOptions(true, route.Label, route.DeleteHandled, route.KeepNotifies);
    }

    /// <summary>"Delete the download": the manager the file came from, the library's linked manager, or Weir itself.</summary>
    public async Task<FileRemovalOutcome> DeleteAsync(UnitOfWork uow, ProcessingFileRecord file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(file);
        var library = await RemuxPassHandler.ResolveLibraryAsync(uow, _libraries, file.LibraryId, null).ConfigureAwait(false);
        if (library is null)
        {
            return await FinishDeleteAsync(uow, file, new RejectRouteOutcome(false, "The library this file belonged to no longer exists.")).ConfigureAwait(false);
        }

        string source;
        try
        {
            source = RemuxPassPaths.ResolveMediaFileUnderRoot(library.WatchedFolder, file.RelativePath);
        }
        catch (ArgumentException exception)
        {
            return await FinishDeleteAsync(uow, file, new RejectRouteOutcome(false, exception.Message)).ConfigureAwait(false);
        }

        var watchedRoot = RemuxPassPaths.Resolve(library.WatchedFolder);
        var route = await ResolveManagerRouteAsync(uow, library, file.RelativePath, cancellationToken).ConfigureAwait(false);

        // Released before any network call or disk delete, the way the automatic reject job releases it (#708):
        // no other lane ever waits on this one for SQLite's write lock.
        await uow.CommitAsync().ConfigureAwait(false);

        // A manager that cannot take the rejection (no capability, or none linked at all) never gets asked: Weir
        // deletes the file itself rather than let ThroughHandoffAsync's own refusal stand in for that.
        var outcome = !route.DeleteHandled
            ? SelfDelete(watchedRoot, source)
            : route.HandoffTarget is not null
                ? await _routes.ThroughHandoffAsync(route.HandoffTarget, route.Origin!, source, watchedRoot, DeleteReason, null, cancellationToken).ConfigureAwait(false)
                : await _routes.ThroughQueueAsync(route.QueueConnections, source, cancellationToken).ConfigureAwait(false);

        return await FinishDeleteAsync(uow, file, outcome).ConfigureAwait(false);
    }

    /// <summary>"Keep the file, but don't process it again": a skip marker, and a hand-off's manager told it will not be imported.</summary>
    public async Task<FileRemovalOutcome> KeepAsync(UnitOfWork uow, ProcessingFileRecord file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(file);
        var library = await RemuxPassHandler.ResolveLibraryAsync(uow, _libraries, file.LibraryId, null).ConfigureAwait(false);
        if (library is null)
        {
            return await FinishKeepAsync(uow, file, new RejectRouteOutcome(false, "The library this file belonged to no longer exists.")).ConfigureAwait(false);
        }

        string source;
        try
        {
            source = RemuxPassPaths.ResolveMediaFileUnderRoot(library.WatchedFolder, file.RelativePath);
        }
        catch (ArgumentException exception)
        {
            return await FinishKeepAsync(uow, file, new RejectRouteOutcome(false, exception.Message)).ConfigureAwait(false);
        }

        if (!File.Exists(source))
        {
            return await FinishKeepAsync(uow, file, new RejectRouteOutcome(false, "The file is no longer in the watched folder, so there is nothing to keep.")).ConfigureAwait(false);
        }

        var route = await ResolveManagerRouteAsync(uow, library, file.RelativePath, cancellationToken).ConfigureAwait(false);
        var fingerprint = SourceFiles.Fingerprint(source);

        // Released before any network call, the same as DeleteAsync above.
        await uow.CommitAsync().ConfigureAwait(false);

        var outcome = route.HandoffTarget is not null
            ? await KeepThroughHandoffAsync(route.HandoffTarget, route.Origin!, cancellationToken).ConfigureAwait(false)
            : new RejectRouteOutcome(true, "Weir will leave this file alone until it changes.");
        if (!outcome.Done)
        {
            return await FinishKeepAsync(uow, file, outcome).ConfigureAwait(false);
        }

        await _skipMarkers.SetAsync(uow, library.Id, file.RelativePath, fingerprint.SizeBytes, fingerprint.ModifiedTimeNs).ConfigureAwait(false);
        return await FinishKeepAsync(uow, file, outcome).ConfigureAwait(false);
    }

    /// <summary>The same failed-report shape a reject sends, worded for a file Weir is keeping rather than rejecting.</summary>
    private async Task<RejectRouteOutcome> KeepThroughHandoffAsync(HandoffReportTarget target, HandoffOrigin origin, CancellationToken cancellationToken)
    {
        var label = target.Connection.Label;
        var result = new WireObject().Set("ok", false).Set("outcome", "failed").Set("reason", KeepReason);
        var body = CompletionReports.BuildCompletionBody(origin, result, rejected: false);
        var delivery = await _reporter.PostHandoffReportAsync(target, body, cancellationToken).ConfigureAwait(false);
        var report = new RejectDeliveryReport(target, body, delivery);
        if (!delivery.Accepted)
        {
            var status = delivery.Status.StartsWith("failed: ", StringComparison.Ordinal) ? delivery.Status["failed: ".Length..] : delivery.Status;
            return new RejectRouteOutcome(false, $"{label} did not accept that this file will not be imported ({status}), so Weir did not keep it.", label, null, report);
        }

        return new RejectRouteOutcome(true, $"Weir will leave this file alone until it changes. {label} was told it will not be imported.", label, null, report);
    }

    private static RejectRouteOutcome SelfDelete(string watchedRoot, string source)
    {
        var cleanup = RemuxPassPaths.CleanupRejectedFile(watchedRoot, source, "delete_file");
        return cleanup.Deleted
            ? new RejectRouteOutcome(true, "Weir deleted the file itself: no linked media manager could remove it.")
            : new RejectRouteOutcome(false, cleanup.Detail);
    }

    private async Task<ManagerRoute> ResolveManagerRouteAsync(UnitOfWork uow, ProcessingLibraryRecord library, string relativePath, CancellationToken cancellationToken)
    {
        var originJson = await HandoffOriginCarry.FindAsync(uow, library.Id, relativePath).ConfigureAwait(false);
        var origin = originJson is not null ? HandoffOrigin.FromPayload(new WireObject().Set("origin", originJson)) : null;
        if (origin is not null && _ports.PortForKind(origin.SourceKey) is { } originPort && !originPort.Capabilities().RemovesQueueItems)
        {
            var (target, _) = await _reporter.ResolveHandoffTargetAsync(uow, origin).ConfigureAwait(false);
            if (target is not null)
            {
                var canReject = await CanRejectThroughHandoffAsync(target, cancellationToken).ConfigureAwait(false);
                return new ManagerRoute(target, origin, [], target.Connection.Label, canReject, true);
            }
        }

        var connectionIds = await _libraries.ManagerConnectionIdsAsync(uow, library.Id).ConfigureAwait(false);
        var linked = await _connections.ConnectionsByIdAsync(uow, connectionIds).ConfigureAwait(false);
        var queueConnections = linked.Where(connection => _ports.PortForKind(connection.Kind)?.Capabilities().RemovesQueueItems == true).ToList();
        return queueConnections.Count > 0
            ? new ManagerRoute(null, null, queueConnections, queueConnections[0].Label, true, false)
            : ManagerRoute.None;
    }

    private async Task<bool> CanRejectThroughHandoffAsync(HandoffReportTarget target, CancellationToken cancellationToken)
    {
        var port = _ports.PortForKind(target.Connection.Kind);
        if (port is null)
        {
            return false;
        }

        var description = await port.DescribeAsync(target.Connection, cancellationToken).ConfigureAwait(false);
        return description.Status == SignalStatus.Reported && description.AdvertisedCapabilities?.Contains(RejectSupportRules.RejectCapability) == true;
    }

    private static bool SourceFileExists(ProcessingLibraryRecord library, string relativePath)
    {
        try
        {
            return File.Exists(RemuxPassPaths.ResolveMediaFileUnderRoot(library.WatchedFolder, relativePath));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private Task<FileRemovalOutcome> FinishDeleteAsync(UnitOfWork uow, ProcessingFileRecord file, RejectRouteOutcome outcome) =>
        FinishAsync(
            uow, file, outcome, ActivityEventTypes.ProcessingFileRemovalDeleted,
            successTitle: fileName => $"{fileName} was removed from History and its download deleted",
            failureTitle: fileName => $"{fileName} could not be deleted");

    private Task<FileRemovalOutcome> FinishKeepAsync(UnitOfWork uow, ProcessingFileRecord file, RejectRouteOutcome outcome) =>
        FinishAsync(
            uow, file, outcome, ActivityEventTypes.ProcessingFileRemovalKept,
            successTitle: fileName => $"{fileName} was kept without processing it again",
            failureTitle: fileName => $"{fileName} could not be kept");

    /// <summary>Records what happened in Activity, forgets the file's row on success, and never leaves a failure unrecorded.</summary>
    private async Task<FileRemovalOutcome> FinishAsync(
        UnitOfWork uow, ProcessingFileRecord file, RejectRouteOutcome outcome, string eventType, Func<string, string> successTitle, Func<string, string> failureTitle)
    {
        if (outcome.Report is { } report)
        {
            await HandoffCompletionReporter.RecordHandoffReportAsync(uow, report.Target, report.Body, report.Delivery, file.RelativePath).ConfigureAwait(false);
        }

        var detail = new WireObject()
            .Set("relative_media_path", file.RelativePath)
            .Set("library_id", file.LibraryId)
            .Set("manager", outcome.Manager)
            .Set("message", outcome.Reason)
            .Set("trigger", "history_remove_dialog")
            .Set("result", outcome.Done ? "success" : "failed");
        foreach (var (key, value) in outcome.DetailOrEmpty.Items)
        {
            detail.Set(key, value);
        }

        if (outcome.Done)
        {
            await _files.ForgetAsync(uow, file.Id).ConfigureAwait(false);
        }

        var fileName = MediaPathNames.Name(file.RelativePath, OperatingSystem.IsWindows());
        var title = outcome.Done ? successTitle(fileName) : failureTitle(fileName);
        await SqliteActivityWriter.RecordAsync(uow, new ActivityEventDraft(
            eventType, "processing", title, WireStrings.Slice(WireJsonWriter.Dumps(detail, WireJsonFormat.Compact), 10_000))).ConfigureAwait(false);
        await uow.CommitAsync().ConfigureAwait(false);
        return new FileRemovalOutcome(outcome.Done, outcome.Reason);
    }
}
