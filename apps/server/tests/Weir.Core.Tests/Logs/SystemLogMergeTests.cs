using Weir.Core.Json;
using Weir.Core.Logs;
using Weir.Core.Paging;

namespace Weir.Core.Tests.Logs;

/// <summary>How the rows of the three sources of System › Logs are ordered, paged and counted together.</summary>
public sealed class SystemLogMergeTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Rows_of_every_source_are_listed_newest_first()
    {
        var slices = Slices(
            events: [Row(SystemLogSource.Event, 1, 5)],
            jobs: [Row(SystemLogSource.Job, 7, 9)],
            server: [Row(SystemLogSource.Server, 3, 7)]);

        var page = SystemLogMerge.Page(slices, new SystemLogFilter(), SystemLogOrder.Newest, SystemLogOrder.NoWorkflowNames, limit: 10);

        Assert.Equal(["event:1", "server:3", "job:7"], page.Rows.Select(row => row.Id));
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public void Rows_with_the_same_time_are_ordered_by_source_then_number_so_a_page_edge_is_never_ambiguous()
    {
        var slices = Slices(
            events: [Row(SystemLogSource.Event, 4, 5), Row(SystemLogSource.Event, 2, 5)],
            jobs: [Row(SystemLogSource.Job, 9, 5)],
            server: [Row(SystemLogSource.Server, 1, 5)]);

        var page = SystemLogMerge.Page(slices, new SystemLogFilter(), SystemLogOrder.Newest, SystemLogOrder.NoWorkflowNames, limit: 10);

        Assert.Equal(["event:4", "event:2", "job:9", "server:1"], page.Rows.Select(row => row.Id));
    }

    [Fact]
    public void A_page_that_does_not_reach_every_row_hands_back_a_cursor_that_starts_at_the_next_one()
    {
        var slices = Slices(
            events: [Row(SystemLogSource.Event, 1, 3), Row(SystemLogSource.Event, 2, 1)],
            jobs: [Row(SystemLogSource.Job, 5, 2)],
            server: []);

        var page = SystemLogMerge.Page(slices, new SystemLogFilter(), SystemLogOrder.Newest, SystemLogOrder.NoWorkflowNames, limit: 2);

        Assert.Equal(["event:2", "job:5"], page.Rows.Select(row => row.Id));
        Assert.True(SystemLogOrder.Newest.TryDecodeCursor(page.NextCursor, out var after));
        Assert.Equal(SystemLogOrder.Newest.KeyOf(page.Rows[^1], SystemLogOrder.NoWorkflowNames), after);
    }

    [Fact]
    public void Rows_of_every_source_are_listed_in_the_order_asked_for_and_the_cursor_continues_that_order()
    {
        var order = new SystemLogOrder(SystemLogSort.Level, SortDirection.Ascending);
        var slices = Slices(
            events: [Row(SystemLogSource.Event, 1, 1, SystemLogLevels.Success), Row(SystemLogSource.Event, 2, 2, SystemLogLevels.Error)],
            jobs: [Row(SystemLogSource.Job, 7, 3, SystemLogLevels.Warning)],
            server: [Row(SystemLogSource.Server, 3, 4, SystemLogLevels.Error)]);

        var page = SystemLogMerge.Page(slices, new SystemLogFilter(), order, SystemLogOrder.NoWorkflowNames, limit: 3);

        Assert.Equal(["server:3", "event:2", "job:7"], page.Rows.Select(row => row.Id));
        Assert.True(order.TryDecodeCursor(page.NextCursor, out var after));
        Assert.Equal(order.KeyOf(page.Rows[^1], SystemLogOrder.NoWorkflowNames), after);
    }

    [Fact]
    public void A_source_the_filter_leaves_out_adds_no_rows_but_still_shows_how_many_it_would_add()
    {
        var slices = Slices(
            events: [Row(SystemLogSource.Event, 1, 5)],
            jobs: [],
            server: [Row(SystemLogSource.Server, 3, 7)],
            jobsMatching: 12);

        var page = SystemLogMerge.Page(slices, new SystemLogFilter { Sources = [SystemLogSource.Event] }, SystemLogOrder.Newest, SystemLogOrder.NoWorkflowNames, limit: 10);

        Assert.Equal(["event:1"], page.Rows.Select(row => row.Id));
        Assert.Equal(12, page.Counts.BySource[SystemLogSources.Job]);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public void Level_and_category_counts_add_up_over_the_sources_the_filter_names()
    {
        var slices = new Dictionary<SystemLogSource, SystemLogSlice>
        {
            [SystemLogSource.Event] = new([], 3, Tally((SystemLogLevels.Error, 1), (SystemLogLevels.Info, 2)), Tally((SystemLogCategories.SignIn, 3))),
            [SystemLogSource.Job] = new([], 4, Tally((SystemLogLevels.Error, 4)), Tally((SystemLogCategories.Processing, 4))),
            [SystemLogSource.Server] = new([], 9, Tally((SystemLogLevels.Warning, 9)), Tally((SystemLogCategories.Weir, 9))),
        };

        var page = SystemLogMerge.Page(slices, new SystemLogFilter { Sources = [SystemLogSource.Event, SystemLogSource.Job] }, SystemLogOrder.Newest, SystemLogOrder.NoWorkflowNames, limit: 10);

        Assert.Equal(5, page.Counts.ByLevel[SystemLogLevels.Error]);
        Assert.Equal(2, page.Counts.ByLevel[SystemLogLevels.Info]);
        Assert.Equal(0, page.Counts.ByLevel[SystemLogLevels.Warning]);
        Assert.Equal(3, page.Counts.ByCategory[SystemLogCategories.SignIn]);
        Assert.Equal(0, page.Counts.ByCategory[SystemLogCategories.Weir]);
        Assert.Equal(7, page.Total);
    }

    [Fact]
    public void A_filter_that_names_no_source_reads_every_one()
    {
        Assert.All(SystemLogSources.All, source => Assert.True(new SystemLogFilter().Selects(source)));
    }

    [Fact]
    public void A_filter_only_one_source_has_leaves_out_the_others()
    {
        Assert.False(new SystemLogFilter { Trigger = "manual" }.CanMatch(SystemLogSource.Job));
        Assert.False(new SystemLogFilter { Trigger = "manual" }.CanMatch(SystemLogSource.Server));
        Assert.True(new SystemLogFilter { Trigger = "manual" }.CanMatch(SystemLogSource.Event));
        Assert.False(new SystemLogFilter { JobStatuses = ["failed"] }.CanMatch(SystemLogSource.Event));
        Assert.False(new SystemLogFilter { WorkflowId = 3 }.CanMatch(SystemLogSource.Server));
        Assert.True(new SystemLogFilter { HasException = true }.CanMatch(SystemLogSource.Server));
        Assert.False(new SystemLogFilter { HasException = true }.CanMatch(SystemLogSource.Job));
    }

    private static SystemLogRow Row(SystemLogSource source, long key, int minutesAgo, string level = SystemLogLevels.Info) => new(
        source,
        key,
        Noon - TimeSpan.FromMinutes(minutesAgo),
        level,
        SystemLogCategories.Weir,
        null,
        "Something happened",
        null,
        new WireObject());

    private static Dictionary<string, long> Tally(params (string Key, long Count)[] counts) => counts.ToDictionary(pair => pair.Key, pair => pair.Count);

    private static Dictionary<SystemLogSource, SystemLogSlice> Slices(
        SystemLogRow[] events,
        SystemLogRow[] jobs,
        SystemLogRow[] server,
        long? jobsMatching = null) => new()
        {
            [SystemLogSource.Event] = new(events, events.Length, Tally(), Tally()),
            [SystemLogSource.Job] = new(jobs, jobsMatching ?? jobs.Length, Tally(), Tally()),
            [SystemLogSource.Server] = new(server, server.Length, Tally(), Tally()),
        };
}
