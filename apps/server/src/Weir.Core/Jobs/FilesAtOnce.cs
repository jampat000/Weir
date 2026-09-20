using System.Globalization;
using Weir.Core.Processing;

namespace Weir.Core.Jobs;

/// <summary>A queued file pass that could start now, as the read-out counts it.</summary>
public sealed record WaitingJobSnapshot(long RunnerCost, long? LibraryId);

/// <summary>A library as the read-out names it.</summary>
public sealed record FilesAtOnceLibrary(long Id, string Name, bool Enabled, long MaxConcurrentFiles);

/// <summary>What is running, what is waiting, and the one thing the waiting files are waiting for (#633).</summary>
/// <param name="FilesAtOnce">The saved "Files at once".</param>
/// <param name="WorkerSlots">Slots this server process started with; it cannot run more than this until it restarts.</param>
/// <param name="Effective">What "Files at once" comes to on this server: the smaller of the two.</param>
/// <param name="Running">Jobs holding a slot now.</param>
/// <param name="Waiting">File passes queued and due.</param>
/// <param name="WaitingFor">One of <see cref="FilesAtOnceRules"/>'s reasons.</param>
/// <param name="Message">The reason in plain words, or empty when nothing is waiting.</param>
/// <param name="SlotsNote">Set when this server has fewer slots than "Files at once" asks for.</param>
public sealed record FilesAtOnceReadout(
    int FilesAtOnce,
    int WorkerSlots,
    int Effective,
    int Running,
    int Waiting,
    string WaitingFor,
    string Message,
    string SlotsNote);

/// <summary>
/// Says which limit a queued file is waiting on (#633). Several limits decide what starts; before this nothing on
/// screen said which one was in the way, so the setting to change was a guess.
/// </summary>
public static class FilesAtOnceRules
{
    public const string Nothing = "nothing";
    public const string WorkersOff = "workers_off";
    public const string Paused = "paused";
    public const string FreeSlot = "free_slot";
    public const string LibraryLimit = "library_limit";
    public const string LibraryClosed = "library_closed";
    public const string ResolutionBudget = "resolution_budget";
    public const string Starting = "starting";

    public static FilesAtOnceReadout Describe(
        long filesAtOnce,
        int workerSlots,
        int running,
        IReadOnlyDictionary<long, int> runningPerLibrary,
        IReadOnlyList<WaitingJobSnapshot> waiting,
        IReadOnlyList<FilesAtOnceLibrary> libraries,
        WorkAdmission admission,
        bool budgetEnabled)
    {
        ArgumentNullException.ThrowIfNull(runningPerLibrary);
        ArgumentNullException.ThrowIfNull(waiting);
        ArgumentNullException.ThrowIfNull(libraries);
        ArgumentNullException.ThrowIfNull(admission);

        var wanted = (int)OperatorSettingsRules.ClampMaxConcurrentFiles(filesAtOnce);
        var slots = Math.Max(0, workerSlots);
        var effective = Math.Min(wanted, slots);
        var slotsNote = slots == 0
            ? "This Weir was started with its workers switched off (WEIR_PROCESSING_WORKER_COUNT is 0), so nothing starts here."
            : slots < wanted
                ? $"This Weir was started with {Count(slots, "worker slot")}, so it runs at most {slots.ToString(CultureInfo.InvariantCulture)} at once. " +
                  "Set WEIR_PROCESSING_WORKER_COUNT higher and restart Weir to use more."
                : string.Empty;

        FilesAtOnceReadout Result(string waitingFor, string message) =>
            new(wanted, slots, effective, Math.Max(0, running), waiting.Count, waitingFor, message, slotsNote);

        if (waiting.Count == 0)
        {
            return Result(Nothing, string.Empty);
        }

        var files = Count(waiting.Count, "file");
        var verb = waiting.Count == 1 ? "is" : "are";
        if (slots == 0)
        {
            return Result(WorkersOff, $"{files} {verb} waiting because this Weir's workers are switched off.");
        }

        if (admission.BlocksProcessing)
        {
            return Result(Paused, $"{files} {verb} waiting because processing is paused.");
        }

        if (running >= effective)
        {
            return Result(FreeSlot, $"{files} {verb} waiting for a free slot: {running.ToString(CultureInfo.InvariantCulture)} of {effective.ToString(CultureInfo.InvariantCulture)} in use.");
        }

        var startable = waiting.Where(job => job.LibraryId is not { } id || !admission.BlockedLibraryIds.Contains(id)).ToList();
        if (startable.Count == 0)
        {
            var byId = libraries.ToDictionary(library => library.Id);
            var held = waiting.Select(job => job.LibraryId).OfType<long>().Distinct().Where(byId.ContainsKey).Select(id => byId[id]).ToList();
            var atLimit = held.FirstOrDefault(library =>
                library.Enabled &&
                runningPerLibrary.GetValueOrDefault(library.Id) >= OperatorSettingsRules.EffectiveLibraryLimit(library.MaxConcurrentFiles, filesAtOnce));
            if (atLimit is not null)
            {
                var cap = OperatorSettingsRules.EffectiveLibraryLimit(atLimit.MaxConcurrentFiles, filesAtOnce);
                return Result(
                    LibraryLimit,
                    $"{files} {verb} waiting: {atLimit.Name} runs {cap.ToString(CultureInfo.InvariantCulture)} at once, and {Count(runningPerLibrary.GetValueOrDefault(atLimit.Id), "file")} of its own " +
                    $"{(runningPerLibrary.GetValueOrDefault(atLimit.Id) == 1 ? "is" : "are")} running. Change that in the library's own settings.");
            }

            var closed = held.FirstOrDefault();
            return Result(
                LibraryClosed,
                closed is null
                    ? $"{files} {verb} waiting for a library that is switched off or outside its schedule."
                    : closed.Enabled
                        ? $"{files} {verb} waiting for {closed.Name}'s schedule to open."
                        : $"{files} {verb} waiting because {closed.Name} is switched off.");
        }

        if (budgetEnabled && startable.All(job => job.RunnerCost > admission.AvailableUnits))
        {
            var cheapest = startable.Min(job => job.RunnerCost);
            return Result(
                ResolutionBudget,
                $"{files} {verb} waiting for the resolution budget: {Math.Max(0, admission.AvailableUnits).ToString(CultureInfo.InvariantCulture)} of " +
                $"{admission.Capacity.ToString(CultureInfo.InvariantCulture)} units are free and the next file needs {cheapest.ToString(CultureInfo.InvariantCulture)}. " +
                "Raise the capacity, or switch the resolution budget off, in Process settings.");
        }

        return Result(Starting, $"{files} {verb} about to start.");
    }

    private static string Count(int number, string noun) =>
        $"{number.ToString(CultureInfo.InvariantCulture)} {noun}{(number == 1 ? string.Empty : "s")}";
}
