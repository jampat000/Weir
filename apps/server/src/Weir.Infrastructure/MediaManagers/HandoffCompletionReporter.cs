using System.Globalization;
using System.Net.Http.Headers;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Core.Media;
using Weir.Core.MediaManagers;
using Weir.Core.Text;
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
    /// <para>
    /// A hand-off of several files is reported once, by whichever pass finishes its last file, and the report covers
    /// them all; the passes before it only record their own file's result. That claim and the report it owes are
    /// persisted together, in one transaction (#667): a crash or exception between claiming and sending never leaves a
    /// hand-off claimed with nothing to send, since the report is already durably owed before delivery is even
    /// attempted, and the heartbeat (<see cref="SendWaitingReportsAsync"/>) sends whatever delivery here did not.
    /// </para>
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

        var relative = result.Get("relative_media_path") is PyStr text ? text.Value
            : payload is PyDict named && named.Get("relative_media_path") is PyStr path ? path.Value : null;
        long? libraryId = payload is PyDict carried && carried.Get("library_id") is PyInt library ? (long)library.Value : null;
        HandoffTargetFinish finish;
        try
        {
            var row = string.IsNullOrEmpty(origin.HandoffId) ? null : await HandoffLedgerStore.FindAsync(uow, origin.SourceKey, origin.HandoffId).ConfigureAwait(false);
            finish = await HandoffTargetStore.FinishAsync(uow, row, relative ?? string.Empty, result).ConfigureAwait(false);
            // Durable regardless of outcome: the next pass to finish must see this file's result even if it is not
            // the one that makes the hand-off ready.
            await uow.CommitAsync().ConfigureAwait(false);
        }
        catch (SqliteException exception)
        {
            await uow.RollbackAsync().ConfigureAwait(false);
            _logger.LogWarning(exception, "Weir could not record the result of {Path} on hand-off {HandoffId}, so it did not report it.", relative, origin.HandoffId);
            return "skipped: this file's result could not be recorded";
        }

        switch (finish.Progress)
        {
            case HandoffTargetProgress.Waiting:
                var unfinished = finish.Targets!.Count(target => target.Result is null);
                return $"skipped: {Plural.Of(unfinished, "other file")} of this hand-off {Plural.Noun(unfinished, "has", "have")} not finished yet";
            case HandoffTargetProgress.AlreadyReported:
                return "skipped: another pass already reported this hand-off";
            case HandoffTargetProgress.NotATarget:
                return "skipped: this file is not one the hand-off covers";
            case HandoffTargetProgress.Untracked:
                return await ReportUntrackedFileAsync(uow, origin, result, relative, libraryId, cancellationToken).ConfigureAwait(false);
            default:
                return await ClaimAndDeliverAsync(uow, origin, finish, result, libraryId, viaCancellation: false, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The report for a hand-off that records no target rows of its own (it arrived before migration 0019, or has no
    /// hand-off id at all): the file's own result, exactly as it has always been reported. No claim is made here, so
    /// there is nothing durable to leave dangling if the manager cannot be reached.
    /// </summary>
    private async Task<string> ReportUntrackedFileAsync(UnitOfWork uow, HandoffOrigin origin, PyDict result, string? relative, long? libraryId, CancellationToken cancellationToken)
    {
        var (target, reason) = await ResolveHandoffTargetAsync(uow, origin).ConfigureAwait(false);
        var outputPath = target is not null && CompletionReports.IsSucceeded(result)
            ? await ManagerOutputPathAsync(target.Connection, origin, result, cancellationToken).ConfigureAwait(false)
            : null;
        var body = CompletionReports.BuildCompletionBody(origin, result, outputPath);
        var outcome = new ReportedOutcome(FileReportState(body), body, relative, relative is null ? [] : [relative], libraryId);
        if (target is null)
        {
            await RecordUntrackedOutcomeAsync(uow, origin, outcome, null).ConfigureAwait(false);
            return $"skipped: {reason}";
        }

        var delivery = await PostHandoffReportAsync(target, body, cancellationToken).ConfigureAwait(false);
        await RecordUntrackedOutcomeAsync(uow, origin, outcome, (target, delivery)).ConfigureAwait(false);
        return delivery.Status;
    }

    /// <summary>The manager did not answer at all, as opposed to answering with a refusal.</summary>
    public static bool IsNotAnswering(HandoffReportDelivery delivery) =>
        delivery is { Accepted: false } && delivery.Status.StartsWith(NotAnsweringPrefix, StringComparison.Ordinal);

    /// <summary>
    /// A report ready to record: the ledger state it means, the body, the path Activity names it by (the file, or the
    /// folder of a hand-off of several files), and the files whose History waits on it while the manager is not answering.
    /// </summary>
    private sealed record ReportedOutcome(string State, PyDict Body, string? Subject, IReadOnlyList<string> Files, long? LibraryId);

    /// <summary>
    /// Keep the ledger and Activity in step with a final outcome recorded outside the claim mechanism (a hand-off with
    /// no target rows of its own), then commit. Never throws. A report the manager did not answer is kept on the
    /// hand-off for the heartbeat to send once it answers, and the History of each file it covers says Weir is
    /// waiting for it, in plain words (#652).
    /// </summary>
    private async Task RecordUntrackedOutcomeAsync(UnitOfWork uow, HandoffOrigin origin, ReportedOutcome outcome, (HandoffReportTarget Target, HandoffReportDelivery Delivery)? report)
    {
        var body = outcome.Body;
        try
        {
            await _ledger.RecordOutcomeAsync(
                uow,
                origin.SourceKey,
                origin.HandoffId,
                outcome.State,
                body.Get("outputPath") is PyStr output ? output.Value : null,
                body.Get("message") is PyStr message ? message.Value : null,
                body.Get("outputFiles") is PyList files ? [.. files.Items.OfType<PyStr>().Select(file => file.Value)] : null).ConfigureAwait(false);
            if (report is { } sent)
            {
                await RecordHandoffReportAsync(uow, sent.Target, body, sent.Delivery, outcome.Subject).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(origin.HandoffId))
                {
                    var waiting = IsNotAnswering(sent.Delivery);
                    var owed = waiting
                        ? new PendingReport(origin.CallbackPath, origin.ReleaseName, origin.LibraryId, body, outcome.LibraryId, outcome.Subject, outcome.Files).ToJson()
                        : null;
                    await HandoffLedgerStore.SetPendingReportAsync(uow, origin.SourceKey, origin.HandoffId, owed).ConfigureAwait(false);
                    if (waiting && outcome.LibraryId is { } library)
                    {
                        foreach (var relativePath in outcome.Files)
                        {
                            await AppendFileSentenceAsync(uow, library, relativePath, ManagerWaitMessages.ReportWaiting(sent.Target.Connection.Name)).ConfigureAwait(false);
                        }
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
