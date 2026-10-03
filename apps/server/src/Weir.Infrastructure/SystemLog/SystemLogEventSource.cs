using Weir.Core.Activity;
using Weir.Core.Logs;
using Weir.Core.Time;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.SystemLog;

/// <summary>
/// The Activity events of System › Logs: Weir's own, the ones that are not about one file (a file's story is on Activity).
/// Filtering is the Activity history's own, so an event is found the same way here as there; the level and category are
/// decided in the query by the rules the rows are read with.
/// </summary>
internal sealed class SystemLogEventSource
{
    private const string Table = "activity_events";
    private const string AboutWeir = "weir";

    private static readonly string LevelSql = SystemLogSql.EventLevel("activity_events.result");
    private static readonly string CategorySql = SystemLogSql.EventCategory("activity_events.event_type");
    private static readonly string AtSql = SystemLogSql.AtText("activity_events.created_at");

    private static readonly SystemLogColumns SortColumns = new(
        SystemLogSource.Event,
        AtSql,
        "activity_events.created_at",
        "activity_events.id",
        LevelSql,
        CategorySql,
        "(SELECT libraries.name FROM libraries WHERE libraries.id = activity_events.library_id)");

    public async Task<SystemLogSlice> ReadAsync(UnitOfWork uow, SystemLogRequest request)
    {
        var filter = request.Filter;
        if (!filter.CanMatch(SystemLogSource.Event))
        {
            return SystemLogSlice.None;
        }

        var shared = SharedConditions(filter);
        var levelOnly = shared.Copy().AddIn(LevelSql, "level", filter.Levels);
        var categoryOnly = shared.Copy().AddIn(CategorySql, "category", filter.Categories);
        var both = levelOnly.Copy().AddIn(CategorySql, "category", filter.Categories);
        var matching = await uow.CountAsync($"SELECT count(*) FROM {Table}{both.WhereText}", both.Parameters).ConfigureAwait(false);
        if (!filter.Selects(SystemLogSource.Event))
        {
            return SystemLogSlice.None with { Matching = matching };
        }

        var levels = await TallyAsync(uow, LevelSql, categoryOnly).ConfigureAwait(false);
        var categories = await TallyAsync(uow, CategorySql, levelOnly).ConfigureAwait(false);
        var workflows = await WorkflowTallyAsync(uow, filter).ConfigureAwait(false);
        var page = both.Copy().AddAfter(SortColumns, request);
        var parameters = page.Parameters.Append(("@take", (object?)request.Take)).ToArray();
        var rows = await uow.QueryAsync(
            $"SELECT {ActivityHistoryStore.Columns} FROM {Table}{page.WhereText}{SortColumns.OrderBy(request.Order)} LIMIT @take",
            ActivityHistoryStore.ReadRow,
            parameters).ConfigureAwait(false);
        return new SystemLogSlice([.. rows.Select(RowOf)], matching, levels, categories) { Workflows = workflows };
    }

    /// <summary>The events of each workflow, with every filter but the workflow applied.</summary>
    private static async Task<Dictionary<long, long>> WorkflowTallyAsync(UnitOfWork uow, SystemLogFilter filter)
    {
        var conditions = SharedConditions(filter with { WorkflowId = null })
            .AddIn(LevelSql, "level", filter.Levels)
            .AddIn(CategorySql, "category", filter.Categories)
            .Add("activity_events.library_id IS NOT NULL");
        var rows = await uow.QueryAsync(
            $"SELECT activity_events.library_id, count(*) FROM {Table}{conditions.WhereText} GROUP BY activity_events.library_id",
            reader => (Workflow: reader.GetInt64(0), Count: reader.GetInt64(1)),
            conditions.Parameters).ConfigureAwait(false);
        return rows.ToDictionary(row => row.Workflow, row => row.Count);
    }

    /// <summary>Everything but the level, the category and the page: what both of those, and their counts, are narrowed by.</summary>
    private static SystemLogConditions SharedConditions(SystemLogFilter filter)
    {
        var activity = new ActivityFilter(
            EventType: filter.EventType,
            Search: filter.Text,
            DateFrom: filter.From is { } from ? Timestamp.FromUtc(from.UtcDateTime) : null,
            DateTo: filter.To is { } to ? Timestamp.FromUtc(to.UtcDateTime) : null,
            Trigger: filter.Trigger,
            Result: filter.Result,
            LibraryId: filter.WorkflowId,
            About: AboutWeir);
        var (where, parameters) = ActivityHistoryStore.Where(activity);
        var conditions = new SystemLogConditions(where, parameters);
        if (filter.JobId is { } jobId)
        {
            // An event names the job that wrote it in its detail, which is JSON only when something wrote it as such.
            conditions.Add("CASE WHEN json_valid(activity_events.detail) THEN json_extract(activity_events.detail, '$.job_id') END = @job_id", ("@job_id", jobId));
        }

        return conditions;
    }

    private static async Task<Dictionary<string, long>> TallyAsync(UnitOfWork uow, string bucket, SystemLogConditions conditions)
    {
        var rows = await uow.QueryAsync(
            $"SELECT {bucket} AS bucket, count(*) FROM {Table}{conditions.WhereText} GROUP BY bucket",
            reader => (Bucket: reader.GetString(0), Count: reader.GetInt64(1)),
            conditions.Parameters).ConfigureAwait(false);
        return rows.ToDictionary(row => row.Bucket, row => row.Count);
    }

    private static SystemLogRow RowOf(ActivityEventRow row) => new(
        SystemLogSource.Event,
        row.Id,
        new DateTimeOffset(row.CreatedAt.AsUtc, TimeSpan.Zero),
        SystemLogRules.EventLevel(row.Result),
        SystemLogRules.EventCategory(row.EventType),
        row.LibraryId,
        row.Title,
        null,
        ActivityHistory.ItemOut(row, null));
}
