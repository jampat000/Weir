namespace Weir.Core.Processing;

/// <summary>Whether one watched file can be processed.</summary>
public sealed record WatchedFileDispatchOutcome(string Verdict, string? BlockedReason = null, string? BlockedConnection = null)
{
    public const string Proceed = "proceed";
    public const string WaitUpstream = "wait_upstream";
    public const string NotHeld = "not_held";
}

/// <summary>
/// Decide whether a watched-folder file can be processed. A block from any manager blocks the file — two
/// connections covering one library is an ordinary 4K-plus-1080p setup, and either of them may be
/// mid-import. Its rows are built by <see cref="ManagerQueueSignals.AttributedRowsForFile"/>, so the reason
/// can name the connection rather than just "a media manager".
/// </summary>
public static class WatchedFileDispatch
{
    public static WatchedFileDispatchOutcome Evaluate(IReadOnlyList<AttributedQueueRow> rows, FileAnchorCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var label = ManagerQueueSignals.BlockingConnectionLabel(rows, candidate);
        return label is not null
            ? new WatchedFileDispatchOutcome(WatchedFileDispatchOutcome.WaitUpstream, $"{label} is still importing this file, so Weir left it alone for now.", label)
            : new WatchedFileDispatchOutcome(WatchedFileDispatchOutcome.Proceed);
    }
}
