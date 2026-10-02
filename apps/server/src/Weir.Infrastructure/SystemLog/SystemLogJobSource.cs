using System.Globalization;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.Logs;
using Weir.Core.Processing;
using Weir.Core.Time;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.SystemLog;

/// <summary>
/// The background jobs of System › Logs. A job is one row that changes as it goes, so it stands in the log where its
/// latest change put it. A scan of the watched folders that finished without a problem is left out, as it is from every
/// list of jobs: periodic scans would outnumber the work, and one that failed, or any job asked for by status, still shows.
/// </summary>
internal sealed class SystemLogJobSource
{
    /// <summary>Where the columns after a job's own are: <see cref="JobsInspectionStore.Read"/> maps the first fifteen.</summary>
    private const int AtTextOrdinal = 15;
    private const int WorkflowOrdinal = 18;

    private const string WorkflowSql = "CASE WHEN json_valid(payload_json) THEN json_extract(payload_json, '$.library_id') END";

    private static readonly string Selected =
        $"WITH job_rows AS (SELECT {ProcessingJobStore.JobColumns}, {SystemLogSql.AtText("updated_at")} AS at_text, " +
        $"{SystemLogSql.JobLevel("status", "last_error")} AS level, {SystemLogSql.JobCategory("job_kind")} AS category, " +
        $"{WorkflowSql} AS workflow_id FROM jobs) SELECT * FROM job_rows";

    private static readonly SystemLogColumns SortColumns = new(
        SystemLogSource.Job,
        "at_text",
        null,
        "id",
        "level",
        "category",
        "(SELECT libraries.name FROM libraries WHERE libraries.id = workflow_id)");

    public async Task<SystemLogSlice> ReadAsync(UnitOfWork uow, SystemLogRequest request)
    {
        var filter = request.Filter;
        if (!filter.CanMatch(SystemLogSource.Job))
        {
            return SystemLogSlice.None;
        }

        var shared = SharedConditions(filter);
        var levelOnly = shared.Copy().AddIn("level", "level", filter.Levels);
        var categoryOnly = shared.Copy().AddIn("category", "category", filter.Categories);
        var both = levelOnly.Copy().AddIn("category", "category", filter.Categories);
        var matching = await uow.CountAsync($"SELECT count(*) FROM ({Selected}{both.WhereText})", both.Parameters).ConfigureAwait(false);
        if (!filter.Selects(SystemLogSource.Job))
        {
            return SystemLogSlice.None with { Matching = matching };
        }

        var levels = await TallyAsync(uow, "level", categoryOnly).ConfigureAwait(false);
        var categories = await TallyAsync(uow, "category", levelOnly).ConfigureAwait(false);
        var page = both.Copy().AddAfter(SortColumns, request);
        var rows = await uow.QueryAsync(
            $"{Selected}{page.WhereText}{SortColumns.OrderBy(request.Order)} LIMIT @take",
            reader => (Job: JobsInspectionStore.Read(reader), At: reader.GetString(AtTextOrdinal), Workflow: SqliteValues.GetInt64OrNull(reader, WorkflowOrdinal)),
            page.Parameters.Append(("@take", (object?)request.Take)).ToArray()).ConfigureAwait(false);
        return new SystemLogSlice([.. rows.Select(row => RowOf(row.Job, row.At, row.Workflow))], matching, levels, categories);
    }

    /// <summary>Everything but the level, the category and the page.</summary>
    private static SystemLogConditions SharedConditions(SystemLogFilter filter)
    {
        var conditions = new SystemLogConditions();
        if (filter.JobStatuses.Count > 0)
        {
            conditions.AddIn("status", "status", filter.JobStatuses);
        }
        else
        {
            conditions.Add("NOT (status = @routine_status AND job_kind = @routine_kind)",
                ("@routine_status", ProcessingJobStatus.Completed),
                ("@routine_kind", ProcessingWatchedFolderScanDispatchJobKinds.ScanDispatch));
        }

        if (filter.WorkflowId is { } workflow)
        {
            conditions.Add("workflow_id = @workflow_id", ("@workflow_id", workflow));
        }

        if (filter.JobId is { } jobId)
        {
            conditions.Add("id = @job_id", ("@job_id", jobId));
        }

        if (filter.From is { } from)
        {
            conditions.Add("at_text >= @from_at", ("@from_at", Timestamp.FromUtc(from.UtcDateTime).ToSqlite()));
        }

        if (filter.To is { } to)
        {
            conditions.Add("at_text <= @to_at", ("@to_at", Timestamp.FromUtc(to.UtcDateTime).ToSqlite()));
        }

        return string.IsNullOrWhiteSpace(filter.Text) ? conditions : AddText(conditions, filter.Text.Trim());
    }

    /// <summary>
    /// A job's text is what a person would look for: the words of its kind and of its status, and the technical text it
    /// was made from. The kind and status are words only in <see cref="SystemLogRules"/>, so the words that match them are
    /// found there and the query asks for those kinds and statuses.
    /// </summary>
    private static SystemLogConditions AddText(SystemLogConditions conditions, string text)
    {
        var kinds = SystemLogRules.JobKinds.Where(rule => Contains(rule.Label, text)).Select(rule => rule.Kind).ToList();
        var statuses = SystemLogRules.JobStatusLabels.Where(label => Contains(label.Value, text)).Select(label => label.Key).ToList();
        var alternatives = new List<string>
        {
            "lower(job_kind) LIKE @text",
            "lower(dedupe_key) LIKE @text",
            "lower(coalesce(last_error, '')) LIKE @text",
            "lower(coalesce(payload_json, '')) LIKE @text",
        };
        var parameters = new List<(string Name, object? Value)> { ("@text", "%" + text.ToLowerInvariant() + "%") };
        AddMembership(alternatives, parameters, "job_kind", "kind_text", kinds);
        AddMembership(alternatives, parameters, "status", "status_text", statuses);
        return conditions.Add("(" + string.Join(" OR ", alternatives) + ")", [.. parameters]);
    }

    private static void AddMembership(List<string> alternatives, List<(string Name, object? Value)> parameters, string column, string name, List<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        var names = values.Select((_, index) => $"@{name}{index}").ToList();
        alternatives.Add($"{column} IN ({string.Join(", ", names)})");
        parameters.AddRange(names.Zip(values, (parameter, value) => (parameter, (object?)value)));
    }

    private static bool Contains(string words, string text) => words.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static async Task<Dictionary<string, long>> TallyAsync(UnitOfWork uow, string bucket, SystemLogConditions conditions)
    {
        var rows = await uow.QueryAsync(
            $"SELECT {bucket}, count(*) FROM ({Selected}{conditions.WhereText}) GROUP BY {bucket}",
            reader => (Bucket: reader.GetString(0), Count: reader.GetInt64(1)),
            conditions.Parameters).ConfigureAwait(false);
        return rows.ToDictionary(row => row.Bucket, row => row.Count);
    }

    private static SystemLogRow RowOf(ProcessingJob job, string atText, long? workflowId)
    {
        var record = ProcessingJobWire.Out(job);
        var at = DateTime.ParseExact(atText, Timestamp.SqliteFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        return new SystemLogRow(
            SystemLogSource.Job,
            job.Id,
            new DateTimeOffset(at, TimeSpan.Zero),
            SystemLogRules.JobLevel(job.Status, job.LastError),
            SystemLogRules.JobCategory(job.JobKind),
            workflowId,
            TitleOf(record),
            DetailOf(job),
            record);
    }

    private static string TitleOf(WireObject record) =>
        WireConvert.Str(record["operator_message"]).TrimEnd('.');

    /// <summary>What the job is, and how many times it has been tried.</summary>
    private static string DetailOf(ProcessingJob job)
    {
        var kind = SystemLogRules.JobKindLabel(job.JobKind);
        return job.AttemptCount > 0 && job.Status != ProcessingJobStatus.Completed
            ? string.Create(CultureInfo.InvariantCulture, $"{kind} · attempt {job.AttemptCount} of {job.MaxAttempts}")
            : kind;
    }
}
