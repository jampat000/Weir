namespace Weir.Core.LibraryMode;

/// <summary>
/// Where one library file stands now, against the current rules: exactly one of these, so the counts add up to the library's
/// files. Left alone beats Cleaning, which beats Can't clean yet, which beats Needs cleaning, which beats Matches.
/// The status is what is true of the file today; what Weir did to it once is history, kept beside it, never a status.
/// </summary>
public static class LibraryFileStatus
{
    /// <summary>A person told Weir to leave this file alone.</summary>
    public const string LeftAlone = "left_alone";

    /// <summary>A library clean for this file is queued or running.</summary>
    public const string Cleaning = "cleaning";

    /// <summary>The rules would change it, but not yet: it is still shared with a download, or Weir cannot read it. Weir tries again by itself.</summary>
    public const string CantCleanYet = "cant_clean_yet";

    /// <summary>It does not match the current rules.</summary>
    public const string NeedsCleaning = "needs_cleaning";

    /// <summary>It is fine as it is.</summary>
    public const string Matches = "matches";

    public static readonly IReadOnlyList<string> All = [NeedsCleaning, Cleaning, Matches, CantCleanYet, LeftAlone];

    public static bool IsKnown(string? status) => status is not null && All.Contains(status);
}

/// <summary>Why a file needs cleaning, when the scan can say. A reason it cannot support is never given.</summary>
public static class LibraryChangeReasons
{
    /// <summary>It was not in the library at the last check.</summary>
    public const string New = "new";

    /// <summary>The file on disk is not the one Weir last saw, or last cleaned.</summary>
    public const string Replaced = "replaced";

    /// <summary>The same file matched before, or Weir cleaned it, and it no longer matches the rules.</summary>
    public const string RulesChanged = "rules_changed";

    /// <summary>What the last scan knew of a file.</summary>
    public sealed record Before(long SizeBytes, long ModifiedTimeUnixSeconds, LibraryFileClassification Classification, string? Reason);

    /// <summary>
    /// The reason a file needs cleaning, or null when it does not, or when nothing on record supports one. Reasons need an
    /// earlier scan to compare with: the first scan of a library, with nothing before it, says none, so a library is not
    /// told that everything in it is new.
    /// </summary>
    /// <param name="classification">What the rules say of the file now.</param>
    /// <param name="hadEarlierScan">Whether the library had any file on record before this scan.</param>
    /// <param name="before">The file as the last scan saw it, or null when it was not there.</param>
    /// <param name="sizeBytes">The file's size on disk now.</param>
    /// <param name="modifiedTimeUnixSeconds">The file's modified time on disk now.</param>
    /// <param name="cleanedAtUnixSeconds">When Weir last cleaned this file, if it has.</param>
    public static string? Decide(
        LibraryFileClassification classification,
        bool hadEarlierScan,
        Before? before,
        long sizeBytes,
        long modifiedTimeUnixSeconds,
        long? cleanedAtUnixSeconds)
    {
        if (classification != LibraryFileClassification.WouldChange || !hadEarlierScan)
        {
            return null;
        }

        if (before is null)
        {
            return New;
        }

        // Weir's own clean replaces the file too, so a file no newer than Weir's own clean is not one somebody put there.
        if (cleanedAtUnixSeconds is { } cleaned)
        {
            return modifiedTimeUnixSeconds > cleaned ? Replaced : RulesChanged;
        }

        if (before.SizeBytes != sizeBytes || before.ModifiedTimeUnixSeconds != modifiedTimeUnixSeconds)
        {
            return Replaced;
        }

        return before.Classification == LibraryFileClassification.Matches ? RulesChanged : before.Reason;
    }
}
