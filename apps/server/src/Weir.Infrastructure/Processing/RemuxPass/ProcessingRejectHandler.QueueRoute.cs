using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Rules;

namespace Weir.Infrastructure.Processing.RemuxPass;

public sealed partial class ProcessingRejectHandler
{
    /// <summary>A manager whose port removes queue items is asked to remove the matching item.</summary>
    private async Task<RejectAttempt> RejectThroughQueueAsync(List<ManagerConnection> connections, string source, CancellationToken cancellationToken)
    {
        var matches = new List<(ManagerConnection Connection, PyDict Row, bool IsFolder, int Index)>();
        var rowsByConnection = new Dictionary<int, List<PyDict>>();
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
                return new RejectAttempt(
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
                var outputPath = PyValues.FirstText(row, "outputPath", "output_path");
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
            return new RejectAttempt(false, "No download in the linked media manager's queue points at this file, so Weir could not reject it safely.");
        }

        if (matches.Count > 1)
        {
            return new RejectAttempt(false, "More than one download in the queue points at this file, so Weir could not tell which one to reject.");
        }

        var (matchedConnection, matchedRow, isFolder, matchedIndex) = matches[0];
        var label = matchedConnection.Label;
        var downloadId = PyValues.FirstText(matchedRow, "downloadId");
        if (downloadId is not null)
        {
            var siblings = rowsByConnection.GetValueOrDefault(matchedIndex, []).Count(row => PyValues.FirstText(row, "downloadId") == downloadId);
            if (siblings > 1)
            {
                return new RejectAttempt(
                    false,
                    $"This file is part of a download that {label} tracks as {siblings} items (a season pack or " +
                    "similar). Rejecting it would delete the others too, so Weir handed the original back instead.",
                    label);
            }
        }

        if (isFolder)
        {
            var downloadFolder = PyValues.FirstText(matchedRow, "outputPath", "output_path") ?? string.Empty;
            var only = SingleVideoFileUnder(downloadFolder);
            if (only is null || !string.Equals(only, RemuxPassPaths.Resolve(source), PathComparison))
            {
                return new RejectAttempt(
                    false,
                    $"The download holds more than this one video file. Rejecting it in {label} would delete the " +
                    "others too, so Weir handed the original back instead.",
                    label);
            }
        }

        var removePort = _ports.PortForKind(matchedConnection.Kind);
        if (removePort is null)
        {
            return new RejectAttempt(false, $"Weir does not know how to ask {label} to remove a download.", label);
        }

        try
        {
            await removePort.RemoveQueueItemAsync(matchedConnection, matchedRow, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is MediaManagerHttpException or IOException)
        {
            return new RejectAttempt(
                false, $"{label} did not accept the rejection, so nothing was removed.", label,
                new PyDict().Set("technical_detail", PyStrings.Slice(exception.Message, 500)));
        }

        return new RejectAttempt(
            true,
            $"{label} removed the download and blocklisted the release, so it will not be grabbed again and {label} can search for a different one.",
            label,
            new PyDict().Set("route", "queue").Set("queue_item", matchedRow.Get("id") ?? PyNull.Instance).Set("download_id", downloadId));
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
