namespace Weir.Core.Processing;

/// <summary>
/// The choices History's remove dialog offers for a failed or rejected title whose file is still in the watched
/// folder (#785). A title that does not qualify for the choice (finished, or its file already gone) is always a
/// plain <see cref="Remove"/>, which is today's "Remove from list" behaviour: forget Weir's row, touch nothing else.
/// </summary>
public static class FileRemovalResolutions
{
    /// <summary>Today's plain remove: forget the row, leave the file exactly as it is.</summary>
    public const string Remove = "remove";

    /// <summary>Delete the download (by the manager it came from, the library's linked manager, or Weir itself) and forget the row.</summary>
    public const string Delete = "delete";

    /// <summary>Keep the file where it is; scans skip it until it changes.</summary>
    public const string Keep = "keep";

    /// <summary>Queue the file to be processed again, the same as "Process again".</summary>
    public const string Retry = "retry";

    /// <summary>The choices a request may name; <see cref="Remove"/> is also what an unnamed choice means.</summary>
    public static readonly IReadOnlyList<string> All = [Remove, Delete, Keep, Retry];
}

/// <summary>
/// What the remove dialog should offer for one title, read before it is shown (#785): whether the title even
/// qualifies for a choice, and — when it does — which manager "delete" would ask, or that Weir would delete the
/// file itself, and whether "keep" has a manager to tell. <paramref name="FingerprintRecorded"/> is false for a row
/// from before migration 0025 (or one whose fingerprint could not be read at the time): Weir has nothing of its own
/// to check the file against, so the dialog must show the owner what is on disk right now —
/// <paramref name="UnconfirmedSizeBytes"/> and <paramref name="UnconfirmedModifiedAt"/> — for them to confirm.
/// </summary>
public sealed record FileRemovalOptions(
    bool RequiresChoice,
    string? ManagerLabel,
    bool DeleteHandledByManager,
    bool KeepNotifiesManager,
    bool FingerprintRecorded,
    long? UnconfirmedSizeBytes,
    string? UnconfirmedModifiedAt)
{
    /// <summary>A title that keeps today's plain confirm: finished, or its file is already gone.</summary>
    public static readonly FileRemovalOptions PlainRemove = new(false, null, false, false, true, null, null);
}

/// <summary>
/// What the remove dialog showed the owner for a title with no recorded fingerprint, echoed back on "delete" or
/// "keep" so the server can check the file on disk still matches before touching anything (#786 follow-up:
/// migration 0025 only records a fingerprint going forward, so every row that failed or was rejected before it
/// shipped has none). <see cref="ModifiedAt"/> is the same whole-second UTC text <c>remove-options</c> sent, so the
/// comparison is an exact string match rather than a lossy round trip through a browser's floating-point numbers.
/// </summary>
public readonly record struct FileRemovalConfirmation(long SizeBytes, string ModifiedAt);
