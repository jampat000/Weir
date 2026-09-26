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
/// file itself, and whether "keep" has a manager to tell.
/// </summary>
public sealed record FileRemovalOptions(bool RequiresChoice, string? ManagerLabel, bool DeleteHandledByManager, bool KeepNotifiesManager)
{
    /// <summary>A title that keeps today's plain confirm: finished, or its file is already gone.</summary>
    public static readonly FileRemovalOptions PlainRemove = new(false, null, false, false);
}
