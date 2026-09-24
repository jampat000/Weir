using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Infrastructure.MediaManagers;

namespace Weir.Infrastructure.Processing.RemuxPass;

public sealed partial class ProcessingRejectHandler
{
    /// <summary>A manager that hands files over gets a failed report with disposition: rejected.</summary>
    private async Task<RejectAttempt> RejectThroughHandoffAsync(
        HandoffReportTarget target, HandoffOrigin origin, string source, string watchedRoot, string reason, string? failureClass, CancellationToken cancellationToken)
    {
        var connection = target.Connection;
        var label = connection.Label;
        var port = _ports.PortForKind(connection.Kind);
        if (port is null)
        {
            return new RejectAttempt(false, $"Weir does not know how to ask {label} what it can do.", label);
        }

        var description = await port.DescribeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (description.Status != SignalStatus.Reported)
        {
            // #652 item 5: a manager that is not answering is said to be not answering, in those words.
            return new RejectAttempt(
                false,
                description.Status == SignalStatus.Unreachable
                    ? ManagerWaitMessages.RejectNotAnswering(label)
                    : description.Detail ?? $"Weir could not ask {label} what it can do.",
                label);
        }

        if (description.AdvertisedCapabilities is null || !description.AdvertisedCapabilities.Contains(RejectSupportRules.RejectCapability))
        {
            return new RejectAttempt(false, $"{label} does not yet say it can replace a rejected release, so Weir did not delete the download.", label);
        }

        if (!File.Exists(source))
        {
            return new RejectAttempt(false, $"The original is no longer at {source}, so there is nothing to reject.");
        }

        var result = new PyDict().Set("ok", false).Set("outcome", "failed").Set("reason", reason);
        if (!string.IsNullOrEmpty(failureClass))
        {
            result.Set("failure_class", failureClass);
        }

        var body = CompletionReports.BuildCompletionBody(origin, result, rejected: true);
        var delivery = await _reporter.PostHandoffReportAsync(target, body, cancellationToken).ConfigureAwait(false);
        var report = new ReportEnvelope(target, body, delivery);
        if (!delivery.Accepted)
        {
            if (HandoffCompletionReporter.IsNotAnswering(delivery))
            {
                return new RejectAttempt(false, $"{ManagerWaitMessages.RejectNotAnswering(label)} Weir kept the download.", label, null, report);
            }

            var status = delivery.Status.StartsWith("failed: ", StringComparison.Ordinal) ? delivery.Status["failed: ".Length..] : delivery.Status;
            return new RejectAttempt(false, $"{label} did not accept the rejection ({status}), so Weir kept the download.", label, null, report);
        }

        var cleanup = RemuxPassPaths.CleanupRejectedFile(watchedRoot, source, "delete_file");
        var sentence = cleanup.Deleted
            ? $"{label} accepted that this release is bad and can find a different one. {cleanup.Detail}"
            : $"{label} accepted that this release is bad, but Weir could not remove the download: {cleanup.Detail} Remove it by hand; Weir will not process it again.";
        return new RejectAttempt(true, sentence, label, new PyDict().Set("route", "handoff").Set("source_removed", cleanup.Deleted), report);
    }
}
