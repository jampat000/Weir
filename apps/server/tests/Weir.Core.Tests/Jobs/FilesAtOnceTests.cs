using Weir.Core.Jobs;
using Weir.Core.Processing;

namespace Weir.Core.Tests.Jobs;

/// <summary>#633: "Files at once" means what it says, and a waiting file says what it is waiting for.</summary>
public sealed class FilesAtOnceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly SuitePauseSettings Suite = new("UTC", false, null, true);

    private static LibraryAdmissionSnapshot Library(long id, long limit, bool enabled = true) =>
        new(id, enabled, false, null, false, null, null, null, limit);

    private static LeasedJobSnapshot Running(long libraryId, long cost = 0) => new(cost, $"{{\"library_id\": {libraryId}}}");

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(11, 10)]
    public void Files_at_once_is_one_to_ten(long raw, long expected) =>
        Assert.Equal(expected, OperatorSettingsRules.ClampMaxConcurrentFiles(raw));

    [Theory]
    [InlineData(0, 5, 5)] // not given its own number: the same as Files at once
    [InlineData(2, 5, 2)] // held below it
    [InlineData(8, 5, 5)] // never above it
    [InlineData(-3, 4, 4)]
    public void A_librarys_own_limit_follows_files_at_once_until_it_is_given_a_number(long library, long filesAtOnce, int expected) =>
        Assert.Equal(expected, OperatorSettingsRules.EffectiveLibraryLimit(library, filesAtOnce));

    [Fact]
    public void A_library_without_its_own_limit_runs_as_many_as_files_at_once_allows()
    {
        // Treating 0 as a limit of 1 would undercut "Files at once" on every install that never opens the library's own field.
        var two = WorkAdmissionRules.Evaluate(Suite, null, [Running(1), Running(1)], [Library(1, 0)], Now, filesAtOnce: 3);
        var three = WorkAdmissionRules.Evaluate(Suite, null, [Running(1), Running(1), Running(1)], [Library(1, 0)], Now, filesAtOnce: 3);

        Assert.DoesNotContain(1L, two.BlockedLibraryIds);
        Assert.Contains(1L, three.BlockedLibraryIds);
    }

    [Fact]
    public void A_library_given_its_own_limit_is_still_held_to_it()
    {
        var admission = WorkAdmissionRules.Evaluate(Suite, null, [Running(1)], [Library(1, 1), Library(2, 0)], Now, filesAtOnce: 4);

        Assert.Contains(1L, admission.BlockedLibraryIds);
        Assert.DoesNotContain(2L, admission.BlockedLibraryIds);
    }

    [Fact]
    public void With_the_resolution_budget_off_every_cost_fits()
    {
        var budget = RunnerBudget.FromSettings(4, 1, 1, 2, 4, 0);
        var running = new[] { Running(1, 4) };

        var on = WorkAdmissionRules.Evaluate(Suite, budget, running, [Library(1, 0)], Now, filesAtOnce: 5, budgetEnabled: true);
        var off = WorkAdmissionRules.Evaluate(Suite, budget, running, [Library(1, 0)], Now, filesAtOnce: 5, budgetEnabled: false);

        Assert.Equal(0, on.AvailableUnits);
        Assert.Equal(int.MaxValue, off.AvailableUnits);
    }

    private static FilesAtOnceReadout Describe(
        long filesAtOnce,
        int slots,
        IReadOnlyList<LeasedJobSnapshot> running,
        IReadOnlyList<WaitingJobSnapshot> waiting,
        IReadOnlyList<LibraryAdmissionSnapshot> libraries,
        bool budgetEnabled = false,
        RunnerBudget? budget = null,
        SuitePauseSettings? suite = null)
    {
        var admission = WorkAdmissionRules.Evaluate(suite ?? Suite, budget, running, libraries, Now, filesAtOnce, budgetEnabled);
        var perLibrary = running
            .Select(job => JobPayload.LibraryIdForAdmission(job.PayloadJson))
            .OfType<long>()
            .GroupBy(id => id)
            .ToDictionary(group => group.Key, group => group.Count());
        var named = libraries
            .Select(library => new FilesAtOnceLibrary(library.Id, library.Id == 1 ? "Movies" : "TV", library.Enabled, library.MaxConcurrentFiles))
            .ToList();
        return FilesAtOnceRules.Describe(filesAtOnce, slots, running.Count, perLibrary, waiting, named, admission, budgetEnabled);
    }

    [Fact]
    public void Nothing_waiting_says_nothing()
    {
        var readout = Describe(3, 10, [Running(1)], [], [Library(1, 0)]);

        Assert.Equal(
            (FilesAtOnceRules.Nothing, string.Empty, 3, 1, 0),
            (readout.WaitingFor, readout.Message, readout.Effective, readout.Running, readout.Waiting));
        Assert.Equal(string.Empty, readout.SlotsNote);
    }

    [Fact]
    public void Every_slot_in_use_is_waiting_for_a_free_slot()
    {
        var readout = Describe(2, 10, [Running(1), Running(1)], [new(0, 1), new(0, 1), new(0, 1)], [Library(1, 0)]);

        Assert.Equal(FilesAtOnceRules.FreeSlot, readout.WaitingFor);
        Assert.Equal("3 files are waiting for a free slot: 2 of 2 in use.", readout.Message);
    }

    [Fact]
    public void A_library_at_its_own_limit_is_named()
    {
        var readout = Describe(4, 10, [Running(1)], [new(0, 1)], [Library(1, 1), Library(2, 0)]);

        Assert.Equal(FilesAtOnceRules.LibraryLimit, readout.WaitingFor);
        Assert.Equal(
            "1 file is waiting: Movies runs 1 at once, and 1 file of its own is running. Change that in the library's own settings.",
            readout.Message);
    }

    [Fact]
    public void A_library_that_is_switched_off_is_named()
    {
        var readout = Describe(4, 10, [], [new(0, 2)], [Library(1, 0), Library(2, 0, enabled: false)]);

        Assert.Equal(FilesAtOnceRules.LibraryClosed, readout.WaitingFor);
        Assert.Equal("1 file is waiting because TV is switched off.", readout.Message);
    }

    [Fact]
    public void The_resolution_budget_is_named_only_when_it_is_on_and_in_the_way()
    {
        var budget = RunnerBudget.FromSettings(4, 1, 1, 2, 4, 0);
        var on = Describe(5, 10, [Running(1, 4)], [new(2, 1)], [Library(1, 0)], budgetEnabled: true, budget: budget);
        var off = Describe(5, 10, [Running(1, 4)], [new(2, 1)], [Library(1, 0)], budgetEnabled: false, budget: budget);

        Assert.Equal(FilesAtOnceRules.ResolutionBudget, on.WaitingFor);
        Assert.Contains("0 of 4 units are free and the next file needs 2", on.Message, StringComparison.Ordinal);
        Assert.Equal((FilesAtOnceRules.Starting, "1 file is about to start."), (off.WaitingFor, off.Message));
    }

    [Fact]
    public void A_pause_is_named_before_anything_else()
    {
        var readout = Describe(1, 10, [Running(1)], [new(0, 1)], [Library(1, 0)], suite: new SuitePauseSettings("UTC", true, null, true));

        Assert.Equal((FilesAtOnceRules.Paused, "1 file is waiting because processing is paused."), (readout.WaitingFor, readout.Message));
    }

    [Fact]
    public void A_server_started_with_its_workers_off_says_so()
    {
        var readout = Describe(3, 0, [], [new(0, 1)], [Library(1, 0)]);

        Assert.Equal((0, FilesAtOnceRules.WorkersOff), (readout.Effective, readout.WaitingFor));
        Assert.Equal("1 file is waiting because this Weir's workers are switched off.", readout.Message);
        Assert.Contains("WEIR_PROCESSING_WORKER_COUNT is 0", readout.SlotsNote, StringComparison.Ordinal);
    }

    [Fact]
    public void A_server_started_with_fewer_slots_than_files_at_once_says_so()
    {
        var readout = Describe(10, 4, [Running(1), Running(1), Running(1), Running(1)], [new(0, 1)], [Library(1, 0)]);

        Assert.Equal((4, FilesAtOnceRules.FreeSlot), (readout.Effective, readout.WaitingFor));
        Assert.Contains("4 of 4 in use", readout.Message, StringComparison.Ordinal);
        Assert.Contains("started with 4 worker slots", readout.SlotsNote, StringComparison.Ordinal);
    }
}
