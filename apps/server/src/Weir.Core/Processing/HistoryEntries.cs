using Weir.Core.Activity;

namespace Weir.Core.Processing;

/// <summary>
/// The two kinds of entry History lists (#695): a new download Weir processed, and a file cleaned where it sits in a
/// library. Every entry names its kind, so History tells them apart without guessing from the shape.
/// </summary>
public static class HistoryEntryKinds
{
    public const string Download = "download";
    public const string LibraryClean = "library_clean";
}

/// <summary>How a library clean ended, read from the Activity event the clean wrote.</summary>
public static class LibraryCleanOutcomes
{
    public const string Cleaned = "cleaned";
    public const string Skipped = "skipped";
    public const string Failed = "failed";

    /// <summary>Every event type a clean writes about one file, with the outcome it stands for.</summary>
    public static readonly IReadOnlyDictionary<string, string> ByEventType = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [LibraryActivityEventTypes.FileCleaned] = Cleaned,
        [LibraryActivityEventTypes.FileSkipped] = Skipped,
        [LibraryActivityEventTypes.FileFailed] = Failed,
    };
}
