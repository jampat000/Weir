namespace Weir.Core.Processing;

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
