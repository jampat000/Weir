using Microsoft.Extensions.Logging;
using Weir.Core.Logs;
using Weir.Core.Paging;

namespace Weir.Infrastructure.Tests.SystemLog;

/// <summary>System › Logs in each order it can be listed in, read from a real database and a real log file.</summary>
public sealed partial class SystemLogReaderTests
{
    private const int RowsPerSource = 12;
    private const int SmallPage = 5;
    private const int WholeLog = 100;

    private static readonly string[] EventTypes = ["auth.login_succeeded", "library.scan_completed", "backups.created"];
    private static readonly string[] EventResults = ["failed", "success", "warning", "running"];
    private static readonly string[] JobStatuses = ["failed", "completed", "pending", "leased"];
    private static readonly string[] JobKinds = ["processing.file.remux_pass.v1", "processing.library.scan.v1", "processing.library.clean.v1"];
    private static readonly string[] ServerLoggers = ["weir.processing", "weir.platform.suite_settings.backups", "weir.library_mode.router"];
    private static readonly LogLevel[] ServerLevels = [LogLevel.Error, LogLevel.Warning, LogLevel.Information];

    public static TheoryData<SystemLogSort, SortDirection> EveryOrder()
    {
        var orders = new TheoryData<SystemLogSort, SortDirection>();
        foreach (var sort in Enum.GetValues<SystemLogSort>())
        {
            orders.Add(sort, SortDirection.Ascending);
            orders.Add(sort, SortDirection.Descending);
        }

        return orders;
    }

    [Theory]
    [MemberData(nameof(EveryOrder))]
    public async Task Rows_come_in_the_order_the_sort_and_direction_ask_for(SystemLogSort sort, SortDirection direction)
    {
        var names = await SeedMixedLog();

        var page = await Read(new SystemLogFilter(), new SystemLogOrder(sort, direction), limit: WholeLog);

        Assert.Equal(RowsPerSource * 3, page.Rows.Count);
        Assert.Equal(
            ExpectedOrder(page.Rows, sort, direction, names).Select(row => row.Id),
            page.Rows.Select(row => row.Id));
    }

    [Theory]
    [MemberData(nameof(EveryOrder))]
    public async Task Paging_in_any_order_visits_every_row_once_and_adds_up_to_the_whole_list(SystemLogSort sort, SortDirection direction)
    {
        await SeedMixedLog();
        var order = new SystemLogOrder(sort, direction);
        var whole = await Read(new SystemLogFilter(), order, limit: WholeLog);

        var walked = new List<string>();
        IReadOnlyList<object?>? after = null;
        var pages = 0;
        do
        {
            var page = await Read(new SystemLogFilter(), order, after, SmallPage);
            walked.AddRange(page.Rows.Select(row => row.Id));
            after = order.TryDecodeCursor(page.NextCursor, out var next) ? next : null;
            pages++;
        }
        while (after is not null && pages < WholeLog);

        Assert.Equal(whole.Rows.Select(row => row.Id), walked);
        Assert.Equal(walked.Count, walked.Distinct().Count());
    }

    [Fact]
    public async Task Rows_of_the_same_level_fall_newest_first_when_descending_and_oldest_first_when_ascending()
    {
        await AddEvent(minutesAgo: 30, "auth.login_failed", "Old failure", result: "failed");
        await AddEvent(minutesAgo: 10, "auth.login_failed", "New failure", result: "failed");
        await AddEvent(minutesAgo: 20, "auth.login_succeeded", "Signed in", result: "success");

        var ascending = await Read(new SystemLogFilter(), new SystemLogOrder(SystemLogSort.Level, SortDirection.Ascending));
        var descending = await Read(new SystemLogFilter(), new SystemLogOrder(SystemLogSort.Level, SortDirection.Descending));

        Assert.Equal(["Old failure", "New failure", "Signed in"], ascending.Rows.Select(row => row.Title));
        Assert.Equal(["Signed in", "New failure", "Old failure"], descending.Rows.Select(row => row.Title));
    }

    [Fact]
    public async Task Rows_listed_by_level_run_from_errors_down_to_successes_when_ascending()
    {
        await AddEvent(minutesAgo: 1, "auth.login_succeeded", "Success", result: "success");
        await AddEvent(minutesAgo: 2, "auth.login_failed", "Error", result: "failed");
        AddServerLine(minutesAgo: 3, LogLevel.Warning, "weir.processing", "Warning");
        AddServerLine(minutesAgo: 4, LogLevel.Information, "weir.processing", "Information");

        var page = await Read(new SystemLogFilter(), new SystemLogOrder(SystemLogSort.Level, SortDirection.Ascending));

        Assert.Equal(
            [SystemLogLevels.Error, SystemLogLevels.Warning, SystemLogLevels.Info, SystemLogLevels.Success],
            page.Rows.Select(row => row.Level));
    }

    [Fact]
    public async Task Rows_with_no_workflow_come_last_whichever_way_the_workflow_sort_runs()
    {
        var names = await AddWorkflows("bravo", "Alpha");
        await AddEvent(minutesAgo: 1, "library.scan_completed", "Bravo scanned", result: "success", libraryId: names["bravo"]);
        await AddEvent(minutesAgo: 2, "auth.login_succeeded", "Signed in", result: "success");
        await AddEvent(minutesAgo: 3, "library.scan_completed", "Alpha scanned", result: "success", libraryId: names["Alpha"]);
        AddServerLine(minutesAgo: 4, LogLevel.Warning, "weir.processing", "A server line");

        var ascending = await Read(new SystemLogFilter(), new SystemLogOrder(SystemLogSort.Workflow, SortDirection.Ascending));
        var descending = await Read(new SystemLogFilter(), new SystemLogOrder(SystemLogSort.Workflow, SortDirection.Descending));

        Assert.Equal(["Alpha scanned", "Bravo scanned", "A server line", "Signed in"], ascending.Rows.Select(row => row.Title));
        Assert.Equal(["Bravo scanned", "Alpha scanned", "Signed in", "A server line"], descending.Rows.Select(row => row.Title));
    }

    [Fact]
    public async Task A_workflow_that_no_longer_exists_sorts_with_the_rows_that_have_no_workflow()
    {
        var names = await AddWorkflows("alpha");
        await AddEvent(minutesAgo: 1, "library.scan_completed", "Gone scanned", result: "success", libraryId: 424242);
        await AddEvent(minutesAgo: 2, "library.scan_completed", "Alpha scanned", result: "success", libraryId: names["alpha"]);

        var page = await Read(new SystemLogFilter(), new SystemLogOrder(SystemLogSort.Workflow, SortDirection.Ascending));

        Assert.Equal(["Alpha scanned", "Gone scanned"], page.Rows.Select(row => row.Title));
    }

    [Fact]
    public async Task A_cursor_made_under_one_sort_is_not_a_cursor_under_another()
    {
        await SeedMixedLog();
        var byLevel = new SystemLogOrder(SystemLogSort.Level, SortDirection.Ascending);
        var cursor = (await Read(new SystemLogFilter(), byLevel, limit: SmallPage)).NextCursor;

        Assert.True(byLevel.TryDecodeCursor(cursor, out _));
        Assert.False(new SystemLogOrder(SystemLogSort.Level, SortDirection.Descending).TryDecodeCursor(cursor, out _));
        Assert.False(new SystemLogOrder(SystemLogSort.Category, SortDirection.Ascending).TryDecodeCursor(cursor, out _));
        Assert.False(SystemLogOrder.Newest.TryDecodeCursor(cursor, out _));
    }

    /// <summary>Events, jobs and server lines that share levels, categories, workflows and instants, so every sort has ties to break.</summary>
    private async Task<Dictionary<long, string>> SeedMixedLog()
    {
        var workflows = await AddWorkflows("alpha", "Bravo", "charlie");
        var ids = workflows.Values.ToList();
        for (var index = 0; index < RowsPerSource; index++)
        {
            var minutesAgo = 10 + (index / 4);
            var workflow = index % 4 == 3 ? (long?)null : ids[index % 3];
            await AddEvent(minutesAgo, EventTypes[index % 3], $"Event {index}", EventResults[index % 4], libraryId: workflow);
            await AddJob(minutesAgo, JobKinds[index % 3], JobStatuses[index % 4], payload: workflow is { } id ? $"{{\"library_id\": {id}}}" : null);
            AddServerLine(minutesAgo, ServerLevels[index % 3], ServerLoggers[(index / 3) % 3], $"Line {index}");
        }

        return workflows.ToDictionary(pair => pair.Value, pair => pair.Key);
    }

    private async Task<Dictionary<string, long>> AddWorkflows(params string[] names)
    {
        var ids = new Dictionary<string, long>();
        foreach (var name in names)
        {
            ids[name] = await _store.WithUnitOfWork(async uow =>
            {
                await uow.ExecuteAsync("INSERT INTO libraries (name, media_type) VALUES (@name, 'movie')", ("@name", name));
                return (await uow.QueryAsync("SELECT id FROM libraries WHERE name = @name", reader => reader.GetInt64(0), ("@name", name)))[0];
            });
        }

        return ids;
    }

    /// <summary>The rows as the order says they should read, worked out directly from what each row shows.</summary>
    private static IEnumerable<SystemLogRow> ExpectedOrder(
        IEnumerable<SystemLogRow> rows,
        SystemLogSort sort,
        SortDirection direction,
        IReadOnlyDictionary<long, string> workflowNames)
    {
        var sign = direction == SortDirection.Descending ? -1 : 1;

        int Compare(SystemLogRow first, SystemLogRow second)
        {
            var byValue = sort switch
            {
                SystemLogSort.Level => SystemLogLevels.RankOf(first.Level).CompareTo(SystemLogLevels.RankOf(second.Level)),
                SystemLogSort.Source => string.CompareOrdinal(SystemLogSources.NameOf(first.Source), SystemLogSources.NameOf(second.Source)),
                SystemLogSort.Category => string.CompareOrdinal(first.Category, second.Category),
                _ => 0,
            };
            if (sort == SystemLogSort.Workflow)
            {
                var firstName = first.WorkflowId is { } a ? workflowNames.GetValueOrDefault(a) : null;
                var secondName = second.WorkflowId is { } b ? workflowNames.GetValueOrDefault(b) : null;
                if ((firstName is null) != (secondName is null))
                {
                    return firstName is null ? 1 : -1;
                }

                byValue = firstName is null ? 0 : string.Compare(firstName, secondName, StringComparison.OrdinalIgnoreCase);
            }

            if (byValue != 0)
            {
                return sign * byValue;
            }

            var byTime = first.At.CompareTo(second.At);
            var bySource = ((int)first.Source).CompareTo((int)second.Source);
            return sign * (byTime != 0 ? byTime : bySource != 0 ? bySource : first.Key.CompareTo(second.Key));
        }

        return rows.Order(Comparer<SystemLogRow>.Create(Compare));
    }
}
