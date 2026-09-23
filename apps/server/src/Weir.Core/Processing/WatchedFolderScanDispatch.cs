using Weir.Core.Time;

namespace Weir.Core.Processing;

/// <summary>Durable job kind for the watched-folder scan that dispatches per-file remux passes.</summary>
public static class ProcessingWatchedFolderScanDispatchJobKinds
{
    public const string ScanDispatch = "processing.watched_folder.remux_scan_dispatch.v1";
}

/// <summary>Why one scan could not be queued once its library is found ("no library" is decided by the
/// caller, which is why it is not a case here).</summary>
public enum ScanDispatchPrerequisiteError
{
    NoSavedWatchedFolder,
    MissingOutputForLiveRemux,
}

/// <summary>Pure prerequisite checks shared by the manual HTTP route and the periodic enqueue tick.</summary>
public static class ScanDispatchPrerequisites
{
    /// <summary>Check a found library has a watched folder, and an output folder when remux jobs will run.</summary>
    public static ScanDispatchPrerequisiteError? Validate(string? watchedFolder, string? outputFolder, bool enqueueRemuxJobs)
    {
        if (string.IsNullOrWhiteSpace(watchedFolder))
        {
            return ScanDispatchPrerequisiteError.NoSavedWatchedFolder;
        }

        if (enqueueRemuxJobs && string.IsNullOrWhiteSpace(outputFolder))
        {
            return ScanDispatchPrerequisiteError.MissingOutputForLiveRemux;
        }

        return null;
    }
}

/// <summary>The scan job's own payload.</summary>
public sealed record ScanDispatchJobPayload(bool EnqueueRemuxJobs, string ScanTrigger, string MediaScope, long? LibraryId)
{
    /// <summary><c>scan_trigger</c> is normalized to one of three values; anything else becomes "manual".</summary>
    public static string NormalizeTrigger(string? raw) =>
        raw is "manual" or "periodic" or "filesystem_event" ? raw : "manual";
}

/// <summary>Whether periodic scanning is switched on for a library and for its media scope.</summary>
public static class ScanDispatchScheduleGate
{
    /// <summary>The library's own on/off switch.</summary>
    public static bool LibraryPeriodicScanEnabled(bool libraryEnabled) => libraryEnabled;

    /// <summary>
    /// Whether periodic scanning is switched on for one scope, from the operator settings singleton's
    /// per-scope <c>movie_schedule_enabled</c> / <c>tv_schedule_enabled</c> column: the switch an operator
    /// toggles on the Processing settings screen (#533). The
    /// <c>WEIR_PROCESSING_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_SCHEDULE_ENABLED</c> environment variable is a
    /// separate global kill switch (see <c>ProcessingWatchedFolderScanDispatchScheduleTask</c> in
    /// Weir.Infrastructure). Manual scans never consult either: they are an explicit, one-off request, not
    /// the recurring timer these switches turn off.
    /// </summary>
    public static bool ScopePeriodicScanEnabled(bool movieScheduleEnabled, bool tvScheduleEnabled, string mediaScope) =>
        ProcessingMediaScopes.Normalize(mediaScope) == ProcessingMediaScopes.Tv ? tvScheduleEnabled : movieScheduleEnabled;
}

/// <summary>A library's admission rules read off its CSV/scalar columns.</summary>
public sealed record LibraryAdmissionRules(
    IReadOnlySet<string> MediaExtensions,
    IReadOnlySet<string> ExcludeMarkers,
    IReadOnlyList<string> IncludePatterns,
    IReadOnlyList<string> ExcludePatterns,
    long MinFileSizeMb,
    long MaxFileSizeMb,
    string RejectedFileAction,
    long MinFileAgeSeconds,
    DateTimeOffset? CreatedAfter,
    DateTimeOffset? CreatedBefore,
    DateTimeOffset? ModifiedAfter,
    DateTimeOffset? ModifiedBefore,
    bool ExcludeHidden,
    bool TopLevelOnly)
{
    private static IReadOnlyList<string> CsvValues(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : [.. csv.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0)];

    /// <summary>Read the rules off a library row, clamping sizes and ages to zero or more.</summary>
    public static LibraryAdmissionRules For(ProcessingLibraryRecord library)
    {
        ArgumentNullException.ThrowIfNull(library);
        return new LibraryAdmissionRules(
            MediaExtensions: CsvValues(library.MediaExtensionsCsv).Select(v => v.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal),
            ExcludeMarkers: CsvValues(library.ExcludeMarkersCsv).Select(v => v.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal),
            IncludePatterns: CsvValues(library.IncludePatternsCsv),
            ExcludePatterns: CsvValues(library.ExcludePatternsCsv),
            MinFileSizeMb: Math.Max(0, library.MinFileSizeMb),
            MaxFileSizeMb: Math.Max(0, library.MaxFileSizeMb),
            RejectedFileAction: string.Equals((library.RejectedFileAction ?? string.Empty).Trim(), "delete_file", StringComparison.OrdinalIgnoreCase) ? "delete_file" : "leave",
            MinFileAgeSeconds: Math.Max(0, library.MinFileAgeSeconds),
            CreatedAfter: library.CreatedAfter?.AsUtc,
            CreatedBefore: library.CreatedBefore?.AsUtc,
            ModifiedAfter: library.ModifiedAfter?.AsUtc,
            ModifiedBefore: library.ModifiedBefore?.AsUtc,
            ExcludeHidden: library.ExcludeHidden,
            TopLevelOnly: library.TopLevelOnly);
    }
}

/// <summary>A settled file the library rules refuse, and the counter it moves.</summary>
public sealed record LibraryAdmissionRejection(string Reason, string Counter);

/// <summary>Facts about one candidate file needed by the admission rejection check, independent of how
/// they were read from disk.</summary>
public sealed record CandidateFileFacts(long SizeBytes, DateTimeOffset? CreatedAt, DateTimeOffset? ModifiedAt);

public static class LibraryAdmission
{
    /// <summary>The plain-language reason and summary counter for a
    /// settled file the library's rules refuse, or <see langword="null"/> when it is admitted.</summary>
    public static LibraryAdmissionRejection? Rejection(
        string relativePath,
        string fileName,
        CandidateFileFacts facts,
        LibraryAdmissionRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var sizeMb = Math.Max(0, facts.SizeBytes) / (1024.0 * 1024.0);
        if (rules.MinFileSizeMb > 0 && facts.SizeBytes < rules.MinFileSizeMb * 1024 * 1024)
        {
            return new LibraryAdmissionRejection(
                $"Skipped because this file is {sizeMb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} MB and the {rules.MinFileSizeMb} MB library minimum is not met.",
                "skipped_below_minimum_file_size");
        }

        if (rules.MaxFileSizeMb > 0 && facts.SizeBytes > rules.MaxFileSizeMb * 1024 * 1024)
        {
            return new LibraryAdmissionRejection(
                $"Skipped because this file is {sizeMb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} MB and exceeds the {rules.MaxFileSizeMb} MB library maximum.",
                "skipped_above_maximum_file_size");
        }

        if (facts.CreatedAt is { } createdAt)
        {
            if (rules.CreatedAfter is { } createdAfter && createdAt < createdAfter)
            {
                return new LibraryAdmissionRejection(
                    $"Skipped because its filesystem creation time ({PyDateTime.FromDateTimeOffset(createdAt).IsoFormat()}) is before this library's allowed window.",
                    "skipped_before_created_window");
            }

            if (rules.CreatedBefore is { } createdBefore && createdAt >= createdBefore)
            {
                return new LibraryAdmissionRejection(
                    $"Skipped because its filesystem creation time ({PyDateTime.FromDateTimeOffset(createdAt).IsoFormat()}) is after this library's allowed window.",
                    "skipped_after_created_window");
            }
        }

        if (facts.ModifiedAt is { } modifiedAt)
        {
            if (rules.ModifiedAfter is { } modifiedAfter && modifiedAt < modifiedAfter)
            {
                return new LibraryAdmissionRejection(
                    $"Skipped because its last-modified time ({PyDateTime.FromDateTimeOffset(modifiedAt).IsoFormat()}) is before this library's allowed window.",
                    "skipped_before_modified_window");
            }

            if (rules.ModifiedBefore is { } modifiedBefore && modifiedAt >= modifiedBefore)
            {
                return new LibraryAdmissionRejection(
                    $"Skipped because its last-modified time ({PyDateTime.FromDateTimeOffset(modifiedAt).IsoFormat()}) is after this library's allowed window.",
                    "skipped_after_modified_window");
            }
        }

        var pathValues = new[] { relativePath.ToLowerInvariant(), fileName.ToLowerInvariant() };
        var includes = rules.IncludePatterns.Select(p => p.Trim().ToLowerInvariant()).Where(p => p.Length > 0).ToList();
        if (includes.Count > 0 && !pathValues.Any(value => includes.Any(pattern => FnMatch(value, pattern))))
        {
            return new LibraryAdmissionRejection("Skipped because its path does not match this library's include patterns.", "skipped_by_include_pattern");
        }

        var excludes = rules.ExcludePatterns.Select(p => p.Trim().ToLowerInvariant()).Where(p => p.Length > 0).ToList();
        if (pathValues.Any(value => excludes.Any(pattern => FnMatch(value, pattern))))
        {
            return new LibraryAdmissionRejection("Skipped because its path matches this library's exclude patterns.", "skipped_by_exclude_pattern");
        }

        return null;
    }

    /// <summary>Shell-style <c>*</c>/<c>?</c>/<c>[seq]</c> glob match, case-sensitive (callers lower-case both sides).</summary>
    internal static bool FnMatch(string value, string pattern) => Glob.IsMatch(value, pattern);
}

/// <summary>Minimal POSIX shell-glob matcher (<c>*</c>, <c>?</c>, <c>[seq]</c>/<c>[!seq]</c>), the subset
/// library include/exclude patterns need.</summary>
internal static class Glob
{
    public static bool IsMatch(string value, string pattern) => IsMatch(value, 0, pattern, 0);

    private static bool IsMatch(string value, int vi, string pattern, int pi)
    {
        while (pi < pattern.Length)
        {
            var pc = pattern[pi];
            if (pc == '*')
            {
                // Collapse consecutive '*' and try every split point.
                while (pi < pattern.Length && pattern[pi] == '*')
                {
                    pi++;
                }

                if (pi == pattern.Length)
                {
                    return true;
                }

                for (var k = vi; k <= value.Length; k++)
                {
                    if (IsMatch(value, k, pattern, pi))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (vi >= value.Length)
            {
                return false;
            }

            if (pc == '?')
            {
                vi++;
                pi++;
                continue;
            }

            if (pc == '[')
            {
                var close = pattern.IndexOf(']', pi + 1);
                if (close < 0)
                {
                    // Unterminated bracket: '[' is matched literally, as shell globs do.
                    if (value[vi] != '[')
                    {
                        return false;
                    }

                    vi++;
                    pi++;
                    continue;
                }

                var negate = pattern[pi + 1] is '!' or '^';
                var setStart = negate ? pi + 2 : pi + 1;
                var matched = false;
                for (var k = setStart; k < close; k++)
                {
                    if (k + 2 < close && pattern[k + 1] == '-')
                    {
                        if (value[vi] >= pattern[k] && value[vi] <= pattern[k + 2])
                        {
                            matched = true;
                        }

                        k += 2;
                    }
                    else if (pattern[k] == value[vi])
                    {
                        matched = true;
                    }
                }

                if (matched == negate)
                {
                    return false;
                }

                vi++;
                pi = close + 1;
                continue;
            }

            if (pattern[pi] != value[vi])
            {
                return false;
            }

            vi++;
            pi++;
        }

        return vi == value.Length;
    }
}

// Size settling (FileSettling/SettlingObservation) lives in Weir.Core.Processing.RemuxPass and is shared with
// the scan rather than duplicated here.

/// <summary>A status and the sentence that explains it.</summary>
public sealed record FileStateVerdict(string Status, string Reason, string? BlockedByConnection = null, DateTimeOffset? HoldUntil = null)
{
    public bool Eligible => Status == ProcessingFileStatuses.Unprocessed;
}

/// <summary>Decide one file's processing state. Pure: the caller supplies the schedule-window, settling and
/// access-probe results it already computed.</summary>
public static class FileStateDecision
{
    public static FileStateVerdict DecideFileState(
        ProcessingLibraryRecord library,
        bool inScheduleWindow,
        double? fileAgeSeconds,
        string? pausedReason,
        DateTimeOffset? pausedUntil,
        DateTimeOffset? windowReopensAt,
        bool sizeIsSettling,
        string? settlingReason,
        DateTimeOffset? settlingStableAt,
        string? accessProblem,
        string? blockedByConnection,
        long? minimumAgeSeconds,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(library);
        var scopeWord = library.MediaType == ProcessingMediaScopes.Tv ? "TV episodes" : "Movies";

        if (!library.Enabled)
        {
            return new FileStateVerdict(ProcessingFileStatuses.Disabled, $"The {library.Name} library is switched off, so Weir is leaving its files alone.");
        }

        if (!string.IsNullOrEmpty(pausedReason))
        {
            return new FileStateVerdict(ProcessingFileStatuses.OutOfSchedule, pausedReason, HoldUntil: pausedUntil);
        }

        if (library.ScheduleEnabled && !inScheduleWindow)
        {
            return new FileStateVerdict(
                ProcessingFileStatuses.OutOfSchedule,
                $"The {library.Name} library only runs inside its scheduled hours, and now is outside them. Weir will pick this up when the window opens.",
                HoldUntil: windowReopensAt);
        }

        var configuredAge = minimumAgeSeconds ?? library.MinFileAgeSeconds;
        var holdSeconds = Math.Max(0, configuredAge) + (Math.Max(0, library.HoldMinutes) * 60);
        if (sizeIsSettling)
        {
            return new FileStateVerdict(
                ProcessingFileStatuses.OnHold,
                settlingReason ?? "This file is still being written to, so Weir is waiting for it to finish before touching it.",
                HoldUntil: settlingStableAt);
        }

        if (holdSeconds > 0 && fileAgeSeconds is { } age && age < holdSeconds)
        {
            var remaining = (long)(holdSeconds - age);
            return new FileStateVerdict(
                ProcessingFileStatuses.OnHold,
                $"This file changed too recently. Weir waits {holdSeconds}s after the last change before processing, so it has about {remaining}s to go.",
                HoldUntil: now.AddSeconds(remaining));
        }

        if (!string.IsNullOrEmpty(accessProblem))
        {
            return new FileStateVerdict(ProcessingFileStatuses.OnHold, accessProblem);
        }

        if (!string.IsNullOrEmpty(blockedByConnection))
        {
            return new FileStateVerdict(
                ProcessingFileStatuses.BlockedUpstream,
                $"{blockedByConnection} is still importing this file, so Weir left it alone for now.",
                BlockedByConnection: blockedByConnection);
        }

        return new FileStateVerdict(ProcessingFileStatuses.Unprocessed, $"Ready to process as part of {scopeWord}.");
    }
}

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

/// <summary>Classify a video's resolution (sd/720p/1080p/4k) from its width, falling back to its height.</summary>
public static class RunnerUnits
{
    private static readonly (int Ceiling, string Name)[] ClassByWidth = [(1200, "sd"), (1900, "720p"), (2600, "1080p")];
    private static readonly (int Ceiling, string Name)[] ClassByHeight = [(700, "sd"), (1000, "720p"), (1500, "1080p")];

    public static string ResolutionClassForDimensions(long? width, long? height)
    {
        if (width is { } w && w > 0)
        {
            foreach (var (ceiling, name) in ClassByWidth)
            {
                if (w < ceiling)
                {
                    return name;
                }
            }

            return "4k";
        }

        if (height is not { } h || h <= 0)
        {
            return "undetermined";
        }

        foreach (var (ceiling, name) in ClassByHeight)
        {
            if (h < ceiling)
            {
                return name;
            }
        }

        return "4k";
    }
}
