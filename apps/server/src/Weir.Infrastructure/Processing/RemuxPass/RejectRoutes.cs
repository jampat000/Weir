using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Rules;
using Weir.Infrastructure.MediaManagers;

namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>What a manager said, or is expected to say, back to <c>PostHandoffReportAsync</c>.</summary>
public sealed record RejectDeliveryReport(HandoffReportTarget Target, WireObject Body, HandoffReportDelivery Delivery);

/// <summary>What one reject attempt came to: whether the download is gone, the sentence to show for it, which
/// manager (if any) was asked, extra detail for Activity, and the hand-off report to record, if one was sent.</summary>
public sealed record RejectRouteOutcome(bool Done, string Reason, string? Manager = null, WireObject? Detail = null, RejectDeliveryReport? Report = null)
{
    public WireObject DetailOrEmpty => Detail ?? new WireObject();
}

/// <summary>
/// The two ways Weir asks a media manager to take back a bad release (#465, #471, #785): through a manager whose
/// port removes queue items directly (Sonarr, Radarr), or by reporting a hand-off's failure to the manager that
/// handed the file over (Deluno and any other external integration). Both <see cref="ProcessingRejectHandler"/>'s
/// automatic reject job and the manual "delete" choice on History's remove dialog call these, so the network calls
/// and the safety checks around them (a download that holds more than this one file, a season pack, and so on) live
/// in exactly one place.
/// </summary>
public sealed class RejectRoutes
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly IMediaManagerPorts _ports;
    private readonly HandoffCompletionReporter _reporter;

    public RejectRoutes(IMediaManagerPorts ports, HandoffCompletionReporter reporter)
    {
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
        _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
    }

    /// <summary>A manager whose port removes queue items is asked to remove the matching item.</summary>
    public async Task<RejectRouteOutcome> ThroughQueueAsync(IReadOnlyList<ManagerConnection> connections, string source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(source);
        var matches = new List<(ManagerConnection Connection, WireObject Row, bool IsFolder, int Index)>();
        var rowsByConnection = new Dictionary<int, List<WireObject>>();
        var wanted = NormalizeStoragePath(source);
        for (var index = 0; index < connections.Count; index++)
        {
            var connection = connections[index];
            var port = _ports.PortForKind(connection.Kind);
            if (port is null)
            {
                continue;
            }

            var signal = await port.QueueRowsAsync(connection, cancellationToken).ConfigureAwait(false);
            if (!signal.IsReported)
            {
                return new RejectRouteOutcome(
                    false,
                    signal.Status == SignalStatus.Unreachable
                        ? ManagerWaitMessages.RejectNotAnswering(connection.Label)
                        : signal.Detail ?? $"Weir could not read {connection.Label}'s queue.",
                    connection.Label);
            }

            var rows = signal.Rows.Select(row => row.Payload).ToList();
            rowsByConnection[index] = rows;
            foreach (var row in rows)
            {
                var outputPath = ManagerValues.FirstText(row, "outputPath", "output_path");
                if (outputPath is null)
                {
                    continue;
                }

                var folder = NormalizeStoragePath(outputPath).TrimEnd('/');
                if (folder == wanted)
                {
                    matches.Add((connection, row, false, index));
                }
                else if (folder.Length > 0 && wanted.StartsWith(folder + "/", StringComparison.Ordinal))
                {
                    matches.Add((connection, row, true, index));
                }
            }
        }

        if (matches.Count == 0)
        {
            return new RejectRouteOutcome(false, "No download in the linked media manager's queue points at this file, so Weir could not reject it safely.");
        }

        if (matches.Count > 1)
        {
            return new RejectRouteOutcome(false, "More than one download in the queue points at this file, so Weir could not tell which one to reject.");
        }

        var (matchedConnection, matchedRow, isFolder, matchedIndex) = matches[0];
        var label = matchedConnection.Label;
        var downloadId = ManagerValues.FirstText(matchedRow, "downloadId");
        if (downloadId is not null)
        {
            var siblings = rowsByConnection.GetValueOrDefault(matchedIndex, []).Count(row => ManagerValues.FirstText(row, "downloadId") == downloadId);
            if (siblings > 1)
            {
                return new RejectRouteOutcome(
                    false,
                    $"This file is part of a download that {label} tracks as {siblings} items (a season pack or " +
                    "similar). Rejecting it would delete the others too, so Weir handed the original back instead.",
                    label);
            }
        }

        if (isFolder)
        {
            var downloadFolder = ManagerValues.FirstText(matchedRow, "outputPath", "output_path") ?? string.Empty;
            var only = SingleVideoFileUnder(downloadFolder);
            if (only is null || !string.Equals(only, RemuxPassPaths.Resolve(source), PathComparison))
            {
                return new RejectRouteOutcome(
                    false,
                    $"The download holds more than this one video file. Rejecting it in {label} would delete the " +
                    "others too, so Weir handed the original back instead.",
                    label);
            }
        }

        var removePort = _ports.PortForKind(matchedConnection.Kind);
        if (removePort is null)
        {
            return new RejectRouteOutcome(false, $"Weir does not know how to ask {label} to remove a download.", label);
        }

        try
        {
            await removePort.RemoveQueueItemAsync(matchedConnection, matchedRow, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is MediaManagerHttpException or IOException)
        {
            return new RejectRouteOutcome(
                false, $"{label} did not accept the rejection, so nothing was removed.", label,
                new WireObject().Set("technical_detail", WireStrings.Slice(exception.Message, 500)));
        }

        return new RejectRouteOutcome(
            true,
            $"{label} removed the download and blocklisted the release, so it will not be grabbed again and {label} can search for a different one.",
            label,
            new WireObject().Set("route", "queue").Set("queue_item", matchedRow.Get("id") ?? WireNull.Instance).Set("download_id", downloadId));
    }

    /// <summary>A manager that hands files over gets a failed report with disposition: rejected.</summary>
    public async Task<RejectRouteOutcome> ThroughHandoffAsync(
        HandoffReportTarget target, HandoffOrigin origin, string source, string watchedRoot, string reason, string? failureClass, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(origin);
        var connection = target.Connection;
        var label = connection.Label;
        var port = _ports.PortForKind(connection.Kind);
        if (port is null)
        {
            return new RejectRouteOutcome(false, $"Weir does not know how to ask {label} what it can do.", label);
        }

        var description = await port.DescribeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (description.Status != SignalStatus.Reported)
        {
            // #652 item 5: a manager that is not answering is said to be not answering, in those words.
            return new RejectRouteOutcome(
                false,
                description.Status == SignalStatus.Unreachable
                    ? ManagerWaitMessages.RejectNotAnswering(label)
                    : description.Detail ?? $"Weir could not ask {label} what it can do.",
                label);
        }

        if (description.AdvertisedCapabilities is null || !description.AdvertisedCapabilities.Contains(RejectSupportRules.RejectCapability))
        {
            return new RejectRouteOutcome(false, $"{label} does not yet say it can replace a rejected release, so Weir did not delete the download.", label);
        }

        if (!File.Exists(source))
        {
            return new RejectRouteOutcome(false, $"The original is no longer at {source}, so there is nothing to reject.");
        }

        var result = new WireObject().Set("ok", false).Set("outcome", "failed").Set("reason", reason);
        if (!string.IsNullOrEmpty(failureClass))
        {
            result.Set("failure_class", failureClass);
        }

        var body = CompletionReports.BuildCompletionBody(origin, result, rejected: true);
        var delivery = await _reporter.PostHandoffReportAsync(target, body, cancellationToken).ConfigureAwait(false);
        var report = new RejectDeliveryReport(target, body, delivery);
        if (!delivery.Accepted)
        {
            if (HandoffCompletionReporter.IsNotAnswering(delivery))
            {
                return new RejectRouteOutcome(false, $"{ManagerWaitMessages.RejectNotAnswering(label)} Weir kept the download.", label, null, report);
            }

            var status = delivery.Status.StartsWith("failed: ", StringComparison.Ordinal) ? delivery.Status["failed: ".Length..] : delivery.Status;
            return new RejectRouteOutcome(false, $"{label} did not accept the rejection ({status}), so Weir kept the download.", label, null, report);
        }

        var cleanup = RemuxPassPaths.CleanupRejectedFile(watchedRoot, source, "delete_file");
        var sentence = cleanup.Deleted
            ? $"{label} accepted that this release is bad and can find a different one. {cleanup.Detail}"
            : $"{label} accepted that this release is bad, but Weir could not remove the download: {cleanup.Detail} Remove it by hand; Weir will not process it again.";
        return new RejectRouteOutcome(true, sentence, label, new WireObject().Set("route", "handoff").Set("source_removed", cleanup.Deleted), report);
    }

    /// <summary>A path in a form that compares equal across slash direction, surrounding spaces and case.</summary>
    private static string NormalizeStoragePath(string path) => path.Replace('\\', '/').Trim().ToLowerInvariant();

    /// <summary>The only video file in a download folder, or null.</summary>
    private static string? SingleVideoFileUnder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        List<string> found;
        try
        {
            found = [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(path => RemuxRules.MediaExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return found.Count == 1 ? RemuxPassPaths.Resolve(found[0]) : null;
    }
}
