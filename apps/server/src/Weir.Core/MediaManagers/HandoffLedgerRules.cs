using Weir.Core.Json;
using Weir.Core.Time;

namespace Weir.Core.MediaManagers;

/// <summary>
/// What Weir tells a manager about one hand-off. <see cref="OutputPath"/> is the one output file, or for a hand-off of
/// several files the folder they were handed back in; <see cref="OutputFiles"/> lists every output file either way.
/// </summary>
public sealed record HandoffStatus(
    string HandoffId,
    string State,
    DateTimeOffset LastChangedAt,
    long? QueuePosition = null,
    DateTimeOffset? ScheduledFor = null,
    string? OutputPath = null,
    string? Message = null,
    IReadOnlyList<string>? OutputFiles = null)
{
    /// <summary>The status as JSON, timestamps in UTC with a <c>Z</c>.</summary>
    public WireObject AsJson() => new WireObject()
        .Set("handoffId", HandoffId)
        .Set("state", State)
        .Set("queuePosition", QueuePosition)
        .Set("scheduledFor", Iso(ScheduledFor))
        .Set("lastChangedUtc", Iso(LastChangedAt))
        .Set("outputPath", OutputPath)
        .Set("outputFiles", HandoffOutputFiles.ToJsonOrNull(OutputFiles))
        .Set("message", Message);

    private static string? Iso(DateTimeOffset? value) =>
        value is { } stamp ? Timestamp.FromDateTimeOffset(stamp.ToUniversalTime()).IsoFormat().Replace("+00:00", "Z", StringComparison.Ordinal) : null;
}

/// <summary>The hand-off vocabulary agreed with Deluno and how a file's state maps onto it.</summary>
public static class HandoffLedgerRules
{
    public const string Queued = "queued";
    public const string Scheduled = "scheduled";
    public const string Working = "working";
    public const string Completed = "completed";
    public const string PassedThrough = "passed-through";
    public const string Failed = "failed";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";

    /// <summary>
    /// A file the workflow's own rules left alone (under its minimum size, say). It is one file's result and never a hand-off's
    /// state: <see cref="Combine"/> reads it as nothing wrong when another file was delivered, and as <see cref="Failed"/> when
    /// nothing was.
    /// </summary>
    public const string Skipped = "skipped";

    /// <summary>States a hand-off does not leave.</summary>
    public static readonly IReadOnlySet<string> TerminalStates = new SortedSet<string>(StringComparer.Ordinal)
    {
        Completed, PassedThrough, Failed, Rejected, Cancelled,
    };

    /// <summary>How long a hand-off's ledger entry is kept.</summary>
    public const int RetentionDays = 90;

    public const string CancelledMessage = "The media manager cancelled this hand-off before Weir started on it.";

    /// <summary>What the manager hears when a person cancelled the hand-off's queued pass in Weir (#643).</summary>
    public const string CancelledInWeirMessage = "Someone cancelled this hand-off in Weir before Weir started on it.";

    /// <summary>
    /// One file's state in the manager's words, and when it would next run if known.
    /// </summary>
    /// <remarks>
    /// A failed file with a retry still owed reads <c>scheduled</c> until that retry is queued, including between
    /// the backoff ending and the next watched-folder scan (#531), because a manager takes <c>failed</c> as final.
    /// A retry is owed exactly when the failure recorded a <c>next_retry_at</c>; <c>failed</c> means no retry remains.
    /// </remarks>
    public static (string State, DateTimeOffset? When) FileState(string status, DateTimeOffset? nextRetryAt)
    {
        return status switch
        {
            "processing" => (Working, null),
            "processed" => (Completed, null),
            "passed_through" => (PassedThrough, null),
            "rejected" => (Rejected, null),
            "cancelled" => (Cancelled, null),
            "processing_failed" => nextRetryAt is { } retryAt ? (Scheduled, retryAt) : (Failed, null),
            "skipped" => (Failed, null),
            "out_of_schedule" => (Scheduled, null),
            _ => (Queued, null),
        };
    }

    /// <summary>
    /// A hand-off covering several files is as far along as its least finished file. A file the workflow's own rules left
    /// alone (<see cref="Skipped"/>) does not fail a hand-off that delivered another file, because the manager imports what
    /// was delivered; when nothing was delivered, it is why the hand-off failed.
    /// </summary>
    public static string Combine(IReadOnlyCollection<string> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        foreach (var waiting in new[] { Working, Queued, Scheduled })
        {
            if (states.Contains(waiting))
            {
                return waiting;
            }
        }

        var delivered = states.Contains(Completed) || states.Contains(PassedThrough);
        if (states.Contains(Skipped) && !delivered)
        {
            return Failed;
        }

        foreach (var outcome in new[] { Failed, Rejected, PassedThrough })
        {
            if (states.Contains(outcome))
            {
                return outcome;
            }
        }

        // Cancelled only when nothing in it was delivered (#643): a pack with one episode cancelled and the rest cleaned is
        // completed, so the manager still imports what Weir wrote. Deluno stops looking for output once it hears "cancelled".
        return states.Contains(Cancelled) && !states.Contains(Completed) ? Cancelled : Completed;
    }
}
