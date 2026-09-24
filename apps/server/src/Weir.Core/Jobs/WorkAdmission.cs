using System.Text.Json;
using Weir.Core.Processing;
using Weir.Core.Settings;
using Weir.Core.Time;

namespace Weir.Core.Jobs;

/// <summary>Wall-clock schedule windows in an IANA zone.</summary>
public static class ScheduleWallClock
{
    /// <summary>
    /// Whether the day/start/end window is open at <paramref name="now"/>; always true when the schedule is off.
    /// A window whose end is before its start runs past midnight.
    /// </summary>
    public static bool TimeWindowActive(
        bool scheduleEnabled,
        string? scheduleDays,
        string? scheduleStart,
        string? scheduleEnd,
        string? timezoneName,
        DateTimeOffset now)
    {
        if (!scheduleEnabled)
        {
            return true;
        }

        var local = TimeZones.ToLocal(now, timezoneName);
        var day = ScheduleGrid.DayNames[ScheduleGrid.MondayFirstDayIndex(local.DayOfWeek)];
        if (!ParseDays(scheduleDays).Contains(day))
        {
            return false;
        }

        var start = ParseHourMinute(scheduleStart, new TimeSpan(0, 0, 0));
        var end = ParseHourMinute(scheduleEnd, new TimeSpan(23, 59, 0));
        var current = new TimeSpan(local.Hour, local.Minute, 0);
        return start <= end ? start <= current && current <= end : current >= start || current <= end;
    }

    private static TimeSpan ParseHourMinute(string? text, TimeSpan fallback)
    {
        var parts = (text ?? string.Empty).Trim().Split(':');
        if (parts.Length != 2 ||
            !ScheduleGrid.TryParseInt(parts[0], out var hour) ||
            !ScheduleGrid.TryParseInt(parts[1], out var minute) ||
            hour is < 0 or > 23 || minute is < 0 or > 59)
        {
            return fallback;
        }

        return new TimeSpan(hour, minute, 0);
    }

    private static HashSet<string> ParseDays(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return [];
        }

        var valid = trimmed.Split(',')
            .Select(token => token.Trim())
            .Where(token => token.Length > 0 && ScheduleGrid.DayNames.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
        return valid.Count > 0 ? valid : [.. ScheduleGrid.DayNames];
    }
}

/// <summary>Total runner capacity and what each resolution class costs.</summary>
public sealed record RunnerBudget(int Capacity, IReadOnlyDictionary<string, int> Costs)
{
    /// <summary>The budget when the operator settings row does not exist.</summary>
    public static RunnerBudget Default { get; } = new(4, new Dictionary<string, int>(StringComparer.Ordinal));

    /// <summary>The budget from stored settings: a capacity of zero means the default of four, and costs are never negative.</summary>
    public static RunnerBudget FromSettings(long capacity, long costSd, long cost720p, long cost1080p, long cost4k, long costUndetermined) =>
        new(
            (int)Math.Clamp(capacity == 0 ? 4 : capacity, 1, int.MaxValue),
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["sd"] = NonNegative(costSd),
                ["720p"] = NonNegative(cost720p),
                ["1080p"] = NonNegative(cost1080p),
                ["4k"] = NonNegative(cost4k),
                ["undetermined"] = NonNegative(costUndetermined),
            });

    public int CostFor(string? resolutionClass)
    {
        var name = (resolutionClass ?? "undetermined").Trim().ToLowerInvariant();
        if (name.Length == 0)
        {
            name = "undetermined";
        }

        if (!Costs.ContainsKey(name))
        {
            name = "undetermined";
        }

        return Math.Max(0, Costs.TryGetValue(name, out var cost) ? cost : 0);
    }

    public int Available(long inUse) => (int)Math.Max(0, Capacity - Math.Max(0, inUse));

    private static int NonNegative(long value) => (int)Math.Clamp(value, 0, int.MaxValue);
}

/// <summary>The <c>suite_settings</c> fields admission reads.</summary>
public sealed record SuitePauseSettings(string? AppTimezone, bool ProcessingPaused, PyDateTime? ProcessingPausedUntil, bool ScanWhilePaused);

/// <summary>The <c>libraries</c> fields admission reads.</summary>
public sealed record LibraryAdmissionSnapshot(
    long Id,
    bool Enabled,
    bool ScheduleEnabled,
    string? ScheduleGrid,
    bool ScheduleHoursLimited,
    string? ScheduleDays,
    string? ScheduleStart,
    string? ScheduleEnd,
    long MaxConcurrentFiles);

/// <summary>The two kinds of work the worker slots run, each on slots of its own, so neither waits for the other (#717).</summary>
public enum WorkLane
{
    /// <summary>Cleaning, passing through or rejecting a file: what "Files at once" and each library's own limit count.</summary>
    Files,

    /// <summary>Looking after libraries: scans and the maintenance sweeps.</summary>
    Upkeep,
}

/// <summary>A leased job as admission counts it.</summary>
public sealed record LeasedJobSnapshot(long RunnerCost, string? PayloadJson, WorkLane Lane = WorkLane.Files);

/// <summary>What a worker is allowed to pick up on this pass.</summary>
public sealed record WorkAdmission(
    PauseState Pause,
    IReadOnlySet<long> BlockedLibraryIds,
    string TimezoneName = "UTC",
    int AvailableUnits = 0,
    int Capacity = 0)
{
    /// <summary>
    /// Libraries whose upkeep (a scan, a sweep) may not start now: switched off, outside their schedule, or already running
    /// one, so a library is never scanned twice at once. The files-at-once limits do not apply to upkeep (#717).
    /// </summary>
    public IReadOnlySet<long> UpkeepBlockedLibraryIds { get; init; } = new HashSet<long>();

    public bool BlocksProcessing => Pause.Paused;

    public bool BlocksDetection => Pause.Paused && !Pause.ScanWhilePaused;

    public bool AllowsJobKind(string jobKind) =>
        !Pause.Paused || (WorkAdmissionRules.IsDetectionJobKind(jobKind) && Pause.ScanWhilePaused);
}

/// <summary>Pure rules deciding which libraries and job kinds a worker pass may admit.</summary>
public static class WorkAdmissionRules
{
    /// <summary>Detection job kinds keep running through a pause when "scan while paused" is on.</summary>
    public static readonly IReadOnlyList<string> DetectionJobKindPrefixes = ["processing.watched_folder.remux_scan_dispatch"];

    public static bool IsDetectionJobKind(string jobKind)
    {
        ArgumentNullException.ThrowIfNull(jobKind);
        return DetectionJobKindPrefixes.Any(prefix => jobKind.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>Whether a library's schedule is open: the grid wins when drawn, else the day/start/end trio.</summary>
    public static bool LibraryWindowOpen(LibraryAdmissionSnapshot library, string? timezoneName, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(library);
        if (!library.ScheduleEnabled)
        {
            return true;
        }

        var grid = (library.ScheduleGrid ?? string.Empty).Trim();
        if (grid.Length > 0)
        {
            return ScheduleGrid.Allows(grid, timezoneName, now);
        }

        if (!library.ScheduleHoursLimited)
        {
            return true;
        }

        return ScheduleWallClock.TimeWindowActive(
            scheduleEnabled: true,
            (library.ScheduleDays ?? string.Empty).Trim(),
            OrDefault(library.ScheduleStart, "00:00").Trim(),
            OrDefault(library.ScheduleEnd, "23:59").Trim(),
            timezoneName,
            now);
    }

    /// <summary>When a library's schedule next opens; only knowable from a grid.</summary>
    public static DateTimeOffset? LibraryWindowReopensAt(LibraryAdmissionSnapshot library, string? timezoneName, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(library);
        var grid = (library.ScheduleGrid ?? string.Empty).Trim();
        return grid.Length == 0 ? null : ScheduleGrid.NextOpenSlot(grid, timezoneName, now);
    }

    /// <summary>
    /// Evaluates the pause, every library window and the runner budget, once per worker pass.
    /// <paramref name="libraries"/> must be ordered by id.
    /// </summary>
    public static WorkAdmission Evaluate(
        SuitePauseSettings? suite,
        RunnerBudget? budget,
        IEnumerable<LeasedJobSnapshot> leasedJobs,
        IEnumerable<LibraryAdmissionSnapshot> libraries,
        DateTimeOffset now,
        long filesAtOnce = 1,
        bool budgetEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(leasedJobs);
        ArgumentNullException.ThrowIfNull(libraries);
        if (suite is null)
        {
            // A missing suite_settings row must not starve every job kind of runner capacity; nothing
            // is running yet, so the whole default budget is free (#540).
            return new WorkAdmission(
                new PauseState(false, null, ScanWhilePaused: true),
                new HashSet<long>(),
                AvailableUnits: RunnerBudget.Default.Available(0),
                Capacity: RunnerBudget.Default.Capacity);
        }

        var timezoneName = (suite.AppTimezone ?? "UTC").Trim();
        if (timezoneName.Length == 0)
        {
            timezoneName = "UTC";
        }

        var pause = PauseState.Resolve(suite.ProcessingPaused, suite.ProcessingPausedUntil, suite.ScanWhilePaused, now.UtcDateTime);
        var effectiveBudget = budget ?? RunnerBudget.Default;

        var (unitsInUse, runningPerLibrary, upkeepBlocked) = Tally(leasedJobs);
        var blocked = new HashSet<long>();
        foreach (var library in libraries)
        {
            if (!library.Enabled || !LibraryWindowOpen(library, timezoneName, now))
            {
                blocked.Add(library.Id);
                upkeepBlocked.Add(library.Id);
                continue;
            }

            // A per-library cap so one library cannot occupy every slot and starve the others. A library that has not
            // been given its own number follows "Files at once" (#633), so an unset field never undercuts that setting.
            var cap = OperatorSettingsRules.EffectiveLibraryLimit(library.MaxConcurrentFiles, filesAtOnce);
            if (runningPerLibrary.GetValueOrDefault(library.Id) >= cap)
            {
                blocked.Add(library.Id);
            }
        }

        // With the resolution budget off (#633) a file needs only a free slot: every cost fits.
        return new WorkAdmission(
            pause,
            blocked,
            timezoneName,
            budgetEnabled ? effectiveBudget.Available(unitsInUse) : int.MaxValue,
            effectiveBudget.Capacity)
        {
            UpkeepBlockedLibraryIds = upkeepBlocked,
        };
    }

    /// <summary>
    /// The budget units and files each library has running, from the file lane only, and the libraries with upkeep running.
    /// Upkeep takes nothing from the budget or the files-at-once count; it only keeps a second one off its library (#717).
    /// </summary>
    private static (long UnitsInUse, Dictionary<long, int> RunningPerLibrary, HashSet<long> UpkeepRunning) Tally(IEnumerable<LeasedJobSnapshot> leasedJobs)
    {
        long unitsInUse = 0;
        var runningPerLibrary = new Dictionary<long, int>();
        var upkeepRunning = new HashSet<long>();
        foreach (var job in leasedJobs)
        {
            var libraryId = JobPayload.LibraryIdForAdmission(job.PayloadJson);
            if (job.Lane == WorkLane.Upkeep)
            {
                if (libraryId is { } busy)
                {
                    upkeepRunning.Add(busy);
                }

                continue;
            }

            unitsInUse += Math.Max(0, job.RunnerCost);
            if (libraryId is { } running)
            {
                runningPerLibrary[running] = runningPerLibrary.GetValueOrDefault(running) + 1;
            }
        }

        return (unitsInUse, runningPerLibrary, upkeepRunning);
    }

    private static string OrDefault(string? value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value;
}

/// <summary>Reads typed fields from a job's JSON payload.</summary>
public static class JobPayload
{
    /// <summary>
    /// The payload's <c>library_id</c> when it is a JSON integer, not a boolean.
    /// </summary>
    /// <remarks>
    /// Booleans are rejected so <c>library_id: true</c> is never counted as library 1 when tallying
    /// jobs running per library (#540).
    /// </remarks>
    public static long? LibraryIdForAdmission(string? payloadJson) => StrictInteger(ParseObject(payloadJson), "library_id");

    /// <summary>A payload object, or null when the text is empty, invalid or not an object.</summary>
    public static JsonElement? ParseObject(string? payloadJson)
    {
        var raw = (payloadJson ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A string property, or null when absent or not a string.</summary>
    public static string? StringProperty(JsonElement? payload, string name) =>
        payload is { } element && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// An integer property that is not a boolean (as the activity classifier reads it), or null.
    /// </summary>
    public static long? StrictInteger(JsonElement? payload, string name) =>
        payload is { } element && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && IsIntegerLiteral(value) && value.TryGetInt64(out var number)
            ? number
            : null;

    /// <summary>An integer property where a JSON boolean also counts (<c>true</c> is 1, <c>false</c> is 0), or null.</summary>
    public static long? LooseInteger(JsonElement? payload, string name)
    {
        if (payload is not { } element || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            JsonValueKind.Number when IsIntegerLiteral(value) && value.TryGetInt64(out var number) => number,
            _ => null,
        };
    }

    /// <summary>Only literals without a fraction or exponent count as integers, so <c>1.0</c> is not an id.</summary>
    private static bool IsIntegerLiteral(JsonElement value)
    {
        var text = value.GetRawText();
        return !text.Contains('.', StringComparison.Ordinal) && !text.Contains('e', StringComparison.Ordinal) && !text.Contains('E', StringComparison.Ordinal);
    }
}
