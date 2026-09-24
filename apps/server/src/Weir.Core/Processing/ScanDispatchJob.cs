using Weir.Core.Jobs;

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

/// <summary>The three states a library's periodic watched-folder scanning can be observed in from outside (#747).</summary>
public static class PeriodicScanStates
{
    /// <summary>The global kill switch, the scope's own switch or the library's own switch is off; never scheduled.</summary>
    public const string Off = "off";

    /// <summary>Every switch is on, but the library's own schedule window is currently closed.</summary>
    public const string OutsideHours = "outside_hours";

    /// <summary>Every switch is on and the library's schedule window is open; the periodic timer ticks for it.</summary>
    public const string Scheduled = "scheduled";
}

/// <summary>
/// A library's periodic watched-folder scanning as reported to clients: one of <see cref="PeriodicScanStates"/>, with the
/// next scan time when one is known. Computed from the same switches the periodic scan-dispatch scheduler reads (the
/// global kill switch, the scope switch, the library's own switch) and the same <see cref="WorkAdmissionRules"/> window
/// the worker uses to admit a library's upkeep, so the field never drifts from the real gates.
/// </summary>
public sealed record PeriodicScanStatus(string State, DateTimeOffset? NextScanAt)
{
    public static PeriodicScanStatus Resolve(
        ProcessingLibraryRecord library,
        bool globalScheduleEnabled,
        bool movieScheduleEnabled,
        bool tvScheduleEnabled,
        string? timezoneName,
        DateTimeOffset now,
        DateTimeOffset? nextPeriodicScanAt)
    {
        ArgumentNullException.ThrowIfNull(library);
        var scope = ProcessingMediaScopes.Normalize(library.MediaType);
        var switchedOn = globalScheduleEnabled
            && ScanDispatchScheduleGate.LibraryPeriodicScanEnabled(library.Enabled)
            && ScanDispatchScheduleGate.ScopePeriodicScanEnabled(movieScheduleEnabled, tvScheduleEnabled, scope);
        if (!switchedOn)
        {
            return new PeriodicScanStatus(PeriodicScanStates.Off, null);
        }

        var window = new LibraryAdmissionSnapshot(
            library.Id, library.Enabled, library.ScheduleEnabled, library.ScheduleGrid, library.ScheduleHoursLimited,
            library.ScheduleDays, library.ScheduleStart, library.ScheduleEnd, library.MaxConcurrentFiles);
        if (!WorkAdmissionRules.LibraryWindowOpen(window, timezoneName, now))
        {
            return new PeriodicScanStatus(PeriodicScanStates.OutsideHours, WorkAdmissionRules.LibraryWindowReopensAt(window, timezoneName, now));
        }

        return new PeriodicScanStatus(PeriodicScanStates.Scheduled, nextPeriodicScanAt);
    }
}
