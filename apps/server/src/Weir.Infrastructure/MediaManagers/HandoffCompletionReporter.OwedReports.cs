using Microsoft.Extensions.Logging;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>Reports Weir owes a manager that was not answering when a hand-off finished (item 5 of #652).</summary>
public sealed partial class HandoffCompletionReporter
{
    /// <summary>
    /// Send every report Weir still owes a manager of this kind because it was not answering when the pass ended (item 5 of
    /// #652). The heartbeat calls this once the manager answers its connection test. A report the manager answers, accepted
    /// or refused, stops being owed, and the History of each file it covers stops saying Weir is waiting; one it still does
    /// not answer stays owed. Each report is committed on its own. Returns how many the manager answered.
    /// </summary>
    public async Task<int> SendWaitingReportsAsync(UnitOfWork uow, string sourceKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var answered = 0;
        foreach (var (handoffId, reportJson) in await HandoffLedgerStore.PendingReportsAsync(uow, sourceKey).ConfigureAwait(false))
        {
            if (PendingReport.Parse(reportJson) is not { } pending)
            {
                await HandoffLedgerStore.SetPendingReportAsync(uow, sourceKey, handoffId, null).ConfigureAwait(false);
                await uow.CommitAsync().ConfigureAwait(false);
                continue;
            }

            var origin = new HandoffOrigin(sourceKey, handoffId, pending.CallbackPath, pending.ReleaseName, pending.ManagerLibraryId);
            var (target, _) = await ResolveHandoffTargetAsync(uow, origin).ConfigureAwait(false);
            if (target is null)
            {
                continue;
            }

            var delivery = await PostHandoffReportAsync(target, pending.Body, cancellationToken).ConfigureAwait(false);
            if (IsNotAnswering(delivery))
            {
                continue;
            }

            await HandoffLedgerStore.SetPendingReportAsync(uow, sourceKey, handoffId, null).ConfigureAwait(false);
            await RecordHandoffReportAsync(uow, target, pending.Body, delivery, pending.Subject).ConfigureAwait(false);
            if (pending.LibraryId is { } libraryId)
            {
                foreach (var relativePath in pending.Files)
                {
                    await ReplaceFileSentenceAsync(
                        uow,
                        libraryId,
                        relativePath,
                        ManagerWaitMessages.ReportWaiting(target.Connection.Name),
                        delivery.Accepted ? ManagerWaitMessages.ReportDelivered(target.Connection.Name) : string.Empty).ConfigureAwait(false);
                }
            }

            await uow.CommitAsync().ConfigureAwait(false);
            LogWaitingReportSent(_logger, target.Connection.Name, handoffId, delivery.Status);
            answered++;
        }

        return answered;
    }

    /// <summary>A file's status reason with one sentence swapped for another (or dropped), so History reads as things are now.</summary>
    private static Task<int> ReplaceFileSentenceAsync(UnitOfWork uow, long libraryId, string relativePath, string sentence, string replacement) =>
        uow.ExecuteAsync(
            "UPDATE files SET status_reason = trim(replace(status_reason, $old, $new)), updated_at = CURRENT_TIMESTAMP " +
            "WHERE library_id = $library AND relative_path = $path AND instr(status_reason, $old) > 0",
            ("$old", sentence),
            ("$new", replacement),
            ("$library", libraryId),
            ("$path", relativePath));

    /// <summary>A file's status reason with a sentence added once.</summary>
    private static Task<int> AppendFileSentenceAsync(UnitOfWork uow, long libraryId, string relativePath, string sentence) =>
        uow.ExecuteAsync(
            "UPDATE files SET status_reason = trim(status_reason || ' ' || $sentence), updated_at = CURRENT_TIMESTAMP " +
            "WHERE library_id = $library AND relative_path = $path AND instr(status_reason, $sentence) = 0",
            ("$sentence", sentence),
            ("$library", libraryId),
            ("$path", relativePath));

    /// <summary>
    /// A report Weir owes a manager that was not answering, as saved on the hand-off row. <see cref="Subject"/> is the path
    /// Activity names the report by (the file, or the folder of a hand-off of several files); <see cref="Files"/> are the
    /// files whose History says Weir is waiting.
    /// </summary>
    private sealed record PendingReport(
        string? CallbackPath, string? ReleaseName, string? ManagerLibraryId, PyDict Body, long? LibraryId, string? Subject, IReadOnlyList<string> Files)
    {
        public string ToJson() => PyJsonWriter.Dumps(
            new PyDict()
                .Set("callback_path", CallbackPath)
                .Set("release_name", ReleaseName)
                .Set("manager_library_id", ManagerLibraryId)
                .Set("body", Body)
                .Set("library_id", LibraryId)
                .Set("relative_media_path", Subject)
                .Set("relative_media_paths", HandoffOutputFiles.ToJson(Files)),
            PyJsonFormat.Compact);

        /// <summary>The saved report, or null when it cannot be read. One saved before it listed its files names only its own file.</summary>
        public static PendingReport? Parse(string json)
        {
            try
            {
                if (PyJsonParser.Parse(json) is not PyDict dict || dict.Get("body") is not PyDict body)
                {
                    return null;
                }

                var subject = HandoffOrigin.OptionalText(dict.Get("relative_media_path"));
                IReadOnlyList<string> files = dict.Get("relative_media_paths") is PyList listed
                    ? [.. listed.Items.OfType<PyStr>().Select(item => item.Value)]
                    : subject is null ? [] : [subject];
                return new PendingReport(
                    HandoffOrigin.OptionalText(dict.Get("callback_path")),
                    HandoffOrigin.OptionalText(dict.Get("release_name")),
                    HandoffOrigin.OptionalText(dict.Get("manager_library_id")),
                    body,
                    dict.Get("library_id") is PyInt library ? (long)library.Value : null,
                    subject,
                    files);
            }
            catch (PyJsonDecodeException)
            {
                return null;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Manager} is answering again; the hand-off report Weir owed it for {HandoffId}: {Status}")]
    private static partial void LogWaitingReportSent(ILogger logger, string manager, string handoffId, string status);
}
