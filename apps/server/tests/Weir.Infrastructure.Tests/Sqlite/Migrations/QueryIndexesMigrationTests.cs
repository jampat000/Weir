using Weir.Core.Activity;
using Weir.Core.Processing;
using Weir.Core.Time;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Sqlite.Migrations;

/// <summary>
/// #714's migration (<c>0018_query_indexes.sql</c>): the indexes exist on a new database and on an upgraded one, and the
/// queries the pages repeat read through them instead of walking or sorting a whole table.
/// </summary>
public sealed class QueryIndexesMigrationTests : IDisposable
{
    private static readonly string[] Indexes =
    [
        "ix_activity_events_event_type_created_at",
        "ix_activity_events_library_id_created_at",
        "ix_activity_events_about_weir_created_at",
        "ix_files_last_seen_at_id",
        "ix_files_library_id_last_seen_at",
        "ix_jobs_updated_at",
    ];

    public static TheoryData<string> IndexNames => new(Indexes);

    private const string TempSort = "USE TEMP B-TREE";

    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;

    public QueryIndexesMigrationTests()
    {
        _database = new SqliteDatabase(_temp.Join("weir.sqlite3"));
        new SchemaMigrator(_database).EnsureAtHead();
    }

    public void Dispose()
    {
        _database.ClearPool();
        _temp.Dispose();
    }

    [Theory]
    [MemberData(nameof(IndexNames))]
    public void A_new_database_has_the_index(string name)
    {
        Assert.True(IndexExists(_database, name));
    }

    [Fact]
    public void A_database_from_an_earlier_release_gains_the_indexes_when_upgraded()
    {
        var earlier = new SqliteDatabase(_temp.Join("earlier.sqlite3"));
        try
        {
            new SchemaMigrator(earlier).EnsureAtBaseline();

            new SchemaMigrator(earlier).EnsureAtHead();

            Assert.All(Indexes, name => Assert.True(IndexExists(earlier, name)));
        }
        finally
        {
            earlier.ClearPool();
        }
    }

    [Fact]
    public void Live_progress_reads_only_its_event_types_recent_rows()
    {
        var plan = Plan(
            LiveProgressStore.RecentProgressSql,
            [("@type", ActivityEventTypes.ProcessingFileProcessingProgress), ("@since", "2026-01-01 00:00:00"), ("@max_rows", 64)]);

        Assert.Contains("USING INDEX ix_activity_events_event_type_created_at (event_type=? AND created_at>?)", plan, StringComparison.Ordinal);
    }

    [Fact]
    public void The_processing_overview_reads_only_its_event_types_last_30_days()
    {
        var plan = Plan(
            OverviewStatsStore.RecentResultsSql,
            [("@type", ActivityEventTypes.ProcessingFileRemuxPassCompleted), ("@since", "2026-01-01 00:00:00")]);

        Assert.Contains("USING INDEX ix_activity_events_event_type_created_at (event_type=? AND created_at>?)", plan, StringComparison.Ordinal);
    }

    [Fact]
    public void The_file_list_for_every_library_is_read_in_order_from_the_index()
    {
        var (sql, parameters) = FileStateStore.ListQuery(new ProcessingFileListFilter());

        var plan = Plan(sql, parameters);

        Assert.Contains("ix_files_last_seen_at_id", plan, StringComparison.Ordinal);
        Assert.DoesNotContain(TempSort, plan, StringComparison.Ordinal);
    }

    [Fact]
    public void The_file_list_for_one_library_is_read_in_order_from_the_index()
    {
        var (sql, parameters) = FileStateStore.ListQuery(new ProcessingFileListFilter { LibraryId = 1 });

        var plan = Plan(sql, parameters);

        Assert.Contains("ix_files_library_id_last_seen_at", plan, StringComparison.Ordinal);
        Assert.DoesNotContain(TempSort, plan, StringComparison.Ordinal);
    }

    [Fact]
    public void Jobs_inspection_reads_the_most_recently_changed_jobs_from_the_index()
    {
        var (sql, parameters) = JobsInspectionStore.RecentQuery(100);

        var plan = Plan(sql, parameters);

        Assert.Contains("ix_jobs_updated_at", plan, StringComparison.Ordinal);
        Assert.DoesNotContain(TempSort, plan, StringComparison.Ordinal);
    }

    [Fact]
    public void Activity_for_one_library_is_listed_and_counted_from_its_index()
    {
        var filter = new ActivityFilter(LibraryId: 1);
        var (pageSql, pageParameters) = ActivityHistoryStore.PageQuery(filter, cursor: null, pageSize: 100);
        var (countSql, countParameters) = ActivityHistoryStore.CountQuery(filter);

        var pagePlan = Plan(pageSql, pageParameters);
        var countPlan = Plan(countSql, countParameters);

        Assert.Contains("ix_activity_events_library_id_created_at", pagePlan, StringComparison.Ordinal);
        Assert.DoesNotContain(TempSort, pagePlan, StringComparison.Ordinal);
        Assert.Contains("ix_activity_events_library_id_created_at", countPlan, StringComparison.Ordinal);
    }

    [Fact]
    public void Weirs_own_events_are_listed_from_the_partial_index()
    {
        SeedMixedActivity();
        var filter = new ActivityFilter(About: "weir");
        var (pageSql, pageParameters) = ActivityHistoryStore.PageQuery(filter, cursor: null, pageSize: 100);

        var pagePlan = Plan(pageSql, pageParameters);

        Assert.Contains("ix_activity_events_about_weir_created_at", pagePlan, StringComparison.Ordinal);
        Assert.DoesNotContain(TempSort, pagePlan, StringComparison.Ordinal);
    }

    [Fact]
    public void A_date_filter_reads_a_range_of_the_created_at_index()
    {
        Assert.True(PyDateTime.TryFromIsoFormat("2026-01-02T03:04:05", out var from));
        Assert.True(PyDateTime.TryFromIsoFormat("2026-01-09T03:04:05", out var to));
        var (sql, parameters) = ActivityHistoryStore.CountQuery(new ActivityFilter(DateFrom: from, DateTo: to));

        var plan = Plan(sql, parameters);

        Assert.Contains("SEARCH activity_events USING COVERING INDEX ix_activity_events_created_at (created_at>? AND created_at<?)", plan, StringComparison.Ordinal);
    }

    /// <summary>Events a quarter of which are not about a file: the partial index is chosen for a table with rows in it.</summary>
    private void SeedMixedActivity()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 4000) " +
            "INSERT INTO activity_events (created_at, event_type, module, title, relative_path, library_id) " +
            "SELECT datetime('2026-01-01', '+' || i || ' minutes'), 'test.event_' || (i % 20), 'test', 'Event', " +
            "CASE WHEN i % 4 = 0 THEN NULL ELSE 'Film ' || i || '/Film.mkv' END, i % 5 FROM n";
        command.ExecuteNonQuery();
    }

    private string Plan(string sql, (string Name, object? Value)[] parameters)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        using var reader = command.ExecuteReader();
        var steps = new List<string>();
        while (reader.Read())
        {
            steps.Add(reader.GetString(3));
        }

        return string.Join(" / ", steps);
    }

    private static bool IndexExists(SqliteDatabase database, string name)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'index' AND name = @name";
        command.Parameters.AddWithValue("@name", name);
        return (long)command.ExecuteScalar()! == 1;
    }
}
