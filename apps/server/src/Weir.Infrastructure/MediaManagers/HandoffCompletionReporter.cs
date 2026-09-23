using System.Globalization;
using System.Net.Http.Headers;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Core.Media;
using Weir.Core.MediaManagers;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>Where, and as whom, to report on one hand-off (<c>HandoffReportTarget</c>).</summary>
public sealed record HandoffReportTarget(ManagerConnection Connection, string Url, IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// Reports a finished hand-off to the manager that asked for it. Processing calls
/// <see cref="ReportHandoffCompletionAsync"/> when a pass ends; the reject policy uses the parts.
/// </summary>
public sealed partial class HandoffCompletionReporter
{
    /// <summary>How <see cref="PostHandoffReportAsync"/> says the manager did not answer at all.</summary>
    private const string NotAnsweringPrefix = "failed: could not reach ";

    private readonly MediaManagerConnectionService _connections;
    private readonly HandoffLedgerStore _ledger;
    private readonly IManagerHttpHandlerFactory _handlers;
    private readonly ILogger _logger;

    public HandoffCompletionReporter(
        MediaManagerConnectionService connections,
        HandoffLedgerStore ledger,
        IManagerHttpHandlerFactory handlers,
        ILogger<HandoffCompletionReporter>? logger = null)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>The manager to report to, or a sentence saying why there is none.</summary>
    public async Task<(HandoffReportTarget? Target, string? Reason)> ResolveHandoffTargetAsync(UnitOfWork uow, HandoffOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (string.IsNullOrEmpty(origin.CallbackPath))
        {
            return (null, "the hand-off named no callback path");
        }

        var connection = await MediaManagerConnectionStore.FirstEnabledForKindAsync(uow, origin.SourceKey).ConfigureAwait(false);
        if (connection is null)
        {
            return (null, $"no enabled {origin.SourceKey} connection is configured to report back to");
        }

        var target = _connections.ResolveCallbackTarget(connection);
        if (target is null)
        {
            return (null, $"the {connection.Name} connection has no address saved");
        }

        var headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = "application/json" };
        if (!string.IsNullOrEmpty(target.ApiKey))
        {
            headers["X-Api-Key"] = target.ApiKey;
        }

        return (
            new HandoffReportTarget(
                new ManagerConnection(connection.Kind, connection.Name, target.BaseUrl, target.ApiKey ?? string.Empty, connection.Id),
                $"{target.BaseUrl}/{origin.CallbackPath.TrimStart('/')}",
                headers),
            null);
    }

    /// <summary>Send the report: one POST, no redirects, never throws.</summary>
    public async Task<HandoffReportDelivery> PostHandoffReportAsync(HandoffReportTarget target, PyDict body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(body);
        var name = target.Connection.Name;
        int status;
        try
        {
            using var client = new HttpClient(_handlers.Handler(followRedirects: false), disposeHandler: false) { Timeout = CompletionReports.Timeout };
            using var request = new HttpRequestMessage(HttpMethod.Post, target.Url)
            {
                // Compact, UTF-8, no ASCII escaping: the body bytes managers already receive.
                Content = new ByteArrayContent(PyJsonWriter.DumpsUtf8(body, PyJsonFormat.Response)),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            foreach (var (header, value) in target.Headers)
            {
                if (!string.Equals(header, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.TryAddWithoutValidation(header, value);
                }
            }

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            status = (int)response.StatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            _logger.LogWarning("Hand-off report to {Url} failed: {Error}", target.Url, exception.Message);
            return new HandoffReportDelivery(false, $"failed: could not reach {name}");
        }

        if (status is >= 200 and < 300)
        {
            return new HandoffReportDelivery(true, $"reported {PyConvert.Str(body["status"])} to {name}");
        }

        _logger.LogWarning("Hand-off report to {Url} returned HTTP {Status}", target.Url, status);
        return new HandoffReportDelivery(false, $"failed: {name} answered HTTP {status.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>Record the report in Activity, in plain words, accepted or not.</summary>
    public static Task RecordHandoffReportAsync(UnitOfWork uow, HandoffReportTarget target, PyDict body, HandoffReportDelivery delivery, string? relativePath)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(delivery);
        var name = target.Connection.Name;
        var fileName = !string.IsNullOrEmpty(relativePath)
            ? MediaPathNames.Name(relativePath, OperatingSystem.IsWindows())
            : body.Get("releaseName") is { IsTruthy: true } release ? PyConvert.Str(release) : "a handed-over file";
        string title;
        if (!delivery.Accepted)
        {
            title = $"Weir could not tell {name} about {fileName}";
        }
        else if (body.Get("disposition") is PyStr { Value: "rejected" })
        {
            title = $"Told {name} that {fileName} is a bad release and was removed";
        }
        else if (body.Get("status") is PyStr { Value: "completed" })
        {
            title = $"Told {name} that {fileName} is ready to import";
        }
        else
        {
            title = $"Told {name} that Weir could not process {fileName}";
        }

        var detail = new PyDict()
            .Set("relative_media_path", relativePath)
            .Set("manager", name)
            .Set("accepted", delivery.Accepted)
            .Set("delivery", delivery.Status)
            .Set("report", body)
            .Set("trigger", "worker")
            .Set("result", delivery.Accepted ? "success" : "failed");
        if (!delivery.Accepted)
        {
            detail.Set(
                "next_action",
                $"Check that {name} is running and that its address and API key are right on the Media managers settings page.");
        }

        return SqliteActivityWriter.RecordAsync(uow, new ActivityEventDraft(
            ActivityEventTypes.ProcessingHandoffReported,
            "processing",
            title,
            PyStrings.Slice(PyJsonWriter.Dumps(detail, PyJsonFormat.Compact), 10_000)));
    }

    /// <summary>
    /// Post the outcome back to the originating manager and record that it did. Returns a short status for logging and
    /// never throws: a manager being unreachable must not fail a pass that succeeded on disk.
    /// Commits <paramref name="uow"/> (or rolls it back when recording fails).
    /// </summary>
    public async Task<string> ReportHandoffCompletionAsync(UnitOfWork uow, string? payloadJson, PyDict result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(result);
        PyJson? payload = null;
        if (!string.IsNullOrEmpty(payloadJson))
        {
            try
            {
                payload = PyJsonParser.Parse(payloadJson);
            }
            catch (PyJsonDecodeException)
            {
                return "skipped: job payload is not readable";
            }
        }

        var origin = HandoffOrigin.FromPayload(payload);
        if (origin is null)
        {
            return "skipped: not a hand-off";
        }

        if (string.IsNullOrEmpty(origin.CallbackPath))
        {
            return "skipped: the hand-off named no callback path";
        }

        var succeeded = CompletionReports.IsSucceeded(result);
        if (!succeeded && result.Get("retry_scheduled") is PyBool { Value: true })
        {
            return "skipped: the failure will be retried, so it is not final yet";
        }

        if (!succeeded && result.Get("pass_through_queued") is PyBool { Value: true })
        {
            return "skipped: the original is being handed back, which will be reported when it is delivered";
        }

        if (!succeeded && result.Get("reject_queued") is PyBool { Value: true })
        {
            return "skipped: the release is being rejected, which reports on its own";
        }

        var (target, reason) = await ResolveHandoffTargetAsync(uow, origin).ConfigureAwait(false);
        if (target is null)
        {
            await RecordOutcomeAsync(uow, origin, CompletionReports.BuildCompletionBody(origin, result), null).ConfigureAwait(false);
            return $"skipped: {reason}";
        }

        var outputPath = succeeded ? await ManagerOutputPathAsync(target.Connection, origin, result, cancellationToken).ConfigureAwait(false) : null;
        var body = CompletionReports.BuildCompletionBody(origin, result, outputPath);
        var delivery = await PostHandoffReportAsync(target, body, cancellationToken).ConfigureAwait(false);
        var relative = result.Get("relative_media_path") is PyStr text ? text.Value : null;
        long? libraryId = payload is PyDict carried && carried.Get("library_id") is PyInt library ? (long)library.Value : null;
        await RecordOutcomeAsync(uow, origin, body, (target, delivery, relative), libraryId).ConfigureAwait(false);
        return delivery.Status;
    }

    /// <summary>The manager did not answer at all, as opposed to answering with a refusal.</summary>
    public static bool IsNotAnswering(HandoffReportDelivery delivery) =>
        delivery is { Accepted: false } && delivery.Status.StartsWith(NotAnsweringPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Send every report Weir still owes a manager of this kind because it was not answering when the pass ended (item 5 of
    /// #652). The heartbeat calls this once the manager answers its connection test. A report the manager answers, accepted
    /// or refused, stops being owed, and the file's History stops saying Weir is waiting; one it still does not answer
    /// stays owed. Each report is committed on its own. Returns how many the manager answered.
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
            await RecordHandoffReportAsync(uow, target, pending.Body, delivery, pending.RelativePath).ConfigureAwait(false);
            if (pending.LibraryId is { } libraryId && pending.RelativePath is { } relativePath)
            {
                await ReplaceFileSentenceAsync(
                    uow,
                    libraryId,
                    relativePath,
                    ManagerWaitMessages.ReportWaiting(target.Connection.Name),
                    delivery.Accepted ? ManagerWaitMessages.ReportDelivered(target.Connection.Name) : string.Empty).ConfigureAwait(false);
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

    /// <summary>A report Weir owes a manager that was not answering, as saved on the hand-off row.</summary>
    private sealed record PendingReport(
        string? CallbackPath, string? ReleaseName, string? ManagerLibraryId, PyDict Body, long? LibraryId, string? RelativePath)
    {
        public string ToJson() => PyJsonWriter.Dumps(
            new PyDict()
                .Set("callback_path", CallbackPath)
                .Set("release_name", ReleaseName)
                .Set("manager_library_id", ManagerLibraryId)
                .Set("body", Body)
                .Set("library_id", LibraryId)
                .Set("relative_media_path", RelativePath),
            PyJsonFormat.Compact);

        public static PendingReport? Parse(string json)
        {
            try
            {
                if (PyJsonParser.Parse(json) is not PyDict dict || dict.Get("body") is not PyDict body)
                {
                    return null;
                }

                return new PendingReport(
                    HandoffOrigin.OptionalText(dict.Get("callback_path")),
                    HandoffOrigin.OptionalText(dict.Get("release_name")),
                    HandoffOrigin.OptionalText(dict.Get("manager_library_id")),
                    body,
                    dict.Get("library_id") is PyInt library ? (long)library.Value : null,
                    HandoffOrigin.OptionalText(dict.Get("relative_media_path")));
            }
            catch (PyJsonDecodeException)
            {
                return null;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Manager} is answering again; the hand-off report Weir owed it for {HandoffId}: {Status}")]
    private static partial void LogWaitingReportSent(ILogger logger, string manager, string handoffId, string status);

    /// <summary>
    /// <paramref name="outputFile"/> rebuilt under the manager's output folder, or null when it is
    /// not under ours.
    /// </summary>
    public static string? TranslateOutputPath(string outputFile, string localOutputFolder, string managerOutputFolder)
    {
        string file;
        string folder;
        try
        {
            file = Path.GetFullPath(outputFile);
            folder = Path.GetFullPath(localOutputFolder);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var fileParts = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Where(p => p.Length > 0).ToList();
        var folderParts = folder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Where(p => p.Length > 0).ToList();
        if (fileParts.Count <= folderParts.Count || !folderParts.Select((part, i) => string.Equals(part, fileParts[i], comparison)).All(same => same))
        {
            return null;
        }

        return CompletionReports.ManagerPathJoin(managerOutputFolder, fileParts.Skip(folderParts.Count));
    }

    /// <summary>The output as the manager will see it, or null to fall back to the local path.</summary>
    private async Task<string?> ManagerOutputPathAsync(ManagerConnection connection, HandoffOrigin origin, PyDict result, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(origin.LibraryId) || result.Get("output_file") is not PyStr outputFile || result.Get("processing_output_folder_resolved") is not PyStr localFolder)
        {
            return null;
        }

        if (_connections.Ports.PortForKind(connection.Kind) is not { } port)
        {
            return null;
        }

        ManagerDescription description;
        try
        {
            description = await port.DescribeAsync(connection, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // An unreadable library list only means the output path cannot be placed; the report still goes out.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogWarning(exception, "Could not read {Label}'s libraries to place the output path.", connection.Label);
            return null;
        }

        foreach (var library in description.Libraries)
        {
            if (library.Key == origin.LibraryId && library.ProcessesBeforeImport && !string.IsNullOrEmpty(library.OutputPath))
            {
                return TranslateOutputPath(outputFile.Value, localFolder.Value, library.OutputPath);
            }
        }

        return null;
    }

    /// <summary>
    /// Keep the ledger and Activity in step with a final outcome, then commit. Never throws. A report
    /// the manager did not answer is kept on the hand-off for the heartbeat to send once it answers, and the file's
    /// History says Weir is waiting for it, in plain words (#652).
    /// </summary>
    private async Task RecordOutcomeAsync(
        UnitOfWork uow, HandoffOrigin origin, PyDict body, (HandoffReportTarget Target, HandoffReportDelivery Delivery, string? Relative)? report, long? libraryId = null)
    {
        try
        {
            string state;
            if (body.Get("status") is PyStr { Value: "completed" })
            {
                state = body.Get("message") is PyStr { Value: CompletionReports.PassThroughAfterFailureMessage }
                    ? HandoffLedgerRules.PassedThrough
                    : HandoffLedgerRules.Completed;
            }
            else
            {
                state = HandoffLedgerRules.Failed;
            }

            await _ledger.RecordOutcomeAsync(
                uow,
                origin.SourceKey,
                origin.HandoffId,
                state,
                body.Get("outputPath") is PyStr output ? output.Value : null,
                body.Get("message") is PyStr message ? message.Value : null).ConfigureAwait(false);
            if (report is { } sent)
            {
                await RecordHandoffReportAsync(uow, sent.Target, body, sent.Delivery, sent.Relative).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(origin.HandoffId))
                {
                    var waiting = IsNotAnswering(sent.Delivery);
                    var owed = waiting
                        ? new PendingReport(origin.CallbackPath, origin.ReleaseName, origin.LibraryId, body, libraryId, sent.Relative).ToJson()
                        : null;
                    await HandoffLedgerStore.SetPendingReportAsync(uow, origin.SourceKey, origin.HandoffId, owed).ConfigureAwait(false);
                    if (waiting && libraryId is { } library && !string.IsNullOrEmpty(sent.Relative))
                    {
                        await AppendFileSentenceAsync(uow, library, sent.Relative, ManagerWaitMessages.ReportWaiting(sent.Target.Connection.Name)).ConfigureAwait(false);
                    }
                }
            }

            await uow.CommitAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException or IOException)
        {
            await uow.RollbackAsync().ConfigureAwait(false);
            _logger.LogWarning(exception, "Could not record the hand-off outcome.");
        }
    }
}
