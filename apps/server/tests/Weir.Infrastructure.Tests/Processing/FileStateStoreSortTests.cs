using Weir.Core.Paging;
using Weir.Core.Processing;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Jobs;

namespace Weir.Infrastructure.Tests.Processing;

/// <summary>The list of files in each order it can be asked for, and paged through by cursor, read from real SQLite.</summary>
public sealed class FileStateStoreSortTests
{
    private const int SmallPage = 4;
    private const int WholeList = 1000;

    private static readonly FileStateStore Store = new();
    private static readonly string[] SeededStatuses = [.. ProcessingFileStatuses.All];

    public static TheoryData<ProcessingFileSort, SortDirection> EveryOrder()
    {
        var orders = new TheoryData<ProcessingFileSort, SortDirection>();
        foreach (var sort in Enum.GetValues<ProcessingFileSort>())
        {
            orders.Add(sort, SortDirection.Ascending);
            orders.Add(sort, SortDirection.Descending);
        }

        return orders;
    }

    [Fact]
    public async Task Files_listed_by_file_follow_their_paths_ignoring_the_case_of_letters_and_ties_fall_by_id()
    {
        using var db = new JobsTestDatabase();
        var library = InsertLibrary(db);
        InsertFile(db, library, "b.mkv");
        InsertFile(db, library, "A.mkv");
        InsertFile(db, library, "a.mkv");
        InsertFile(db, library, "C.mkv");

        var ascending = await Paths(db, ProcessingFileSort.File, SortDirection.Ascending);
        var descending = await Paths(db, ProcessingFileSort.File, SortDirection.Descending);

        Assert.Equal(["A.mkv", "a.mkv", "b.mkv", "C.mkv"], ascending);
        Assert.Equal(["C.mkv", "b.mkv", "a.mkv", "A.mkv"], descending);
    }

    [Fact]
    public async Task Files_listed_by_status_follow_what_the_status_means_and_then_the_status_word()
    {
        using var db = new JobsTestDatabase();
        var library = InsertLibrary(db);
        foreach (var status in SeededStatuses.Reverse())
        {
            InsertFile(db, library, $"{status}.mkv", status);
        }

        var ascending = await Paths(db, ProcessingFileSort.Status, SortDirection.Ascending);

        Assert.Equal(
            [
                "processed", "out_of_schedule", "unprocessed", "processing",
                "blocked_upstream", "on_hold", "passed_through", "rejected",
                "processing_failed", "cancelled", "disabled", "skipped",
            ],
            ascending.Select(path => path[..^".mkv".Length]));
        Assert.Equal(ascending.AsEnumerable().Reverse(), await Paths(db, ProcessingFileSort.Status, SortDirection.Descending));
    }

    [Fact]
    public async Task Files_listed_by_when_follow_the_time_they_last_changed_and_files_changed_together_fall_by_id()
    {
        using var db = new JobsTestDatabase();
        var library = InsertLibrary(db);
        InsertFile(db, library, "middle-1.mkv", updatedAt: "2026-10-02 10:00:00");
        InsertFile(db, library, "oldest.mkv", updatedAt: "2026-10-01 09:00:00");
        InsertFile(db, library, "middle-2.mkv", updatedAt: "2026-10-02 10:00:00");
        InsertFile(db, library, "newest.mkv", updatedAt: "2026-10-03 08:00:00");

        var ascending = await Paths(db, ProcessingFileSort.When, SortDirection.Ascending);
        var descending = await Paths(db, ProcessingFileSort.When, SortDirection.Descending);

        Assert.Equal(["oldest.mkv", "middle-1.mkv", "middle-2.mkv", "newest.mkv"], ascending);
        Assert.Equal(["newest.mkv", "middle-2.mkv", "middle-1.mkv", "oldest.mkv"], descending);
    }

    [Fact]
    public async Task A_list_with_no_sort_is_newest_seen_first_and_a_file_never_seen_comes_last()
    {
        using var db = new JobsTestDatabase();
        var library = InsertLibrary(db);
        InsertFile(db, library, "seen-early.mkv", lastSeenAt: "2026-10-01 09:00:00");
        InsertFile(db, library, "never-seen.mkv", lastSeenAt: null);
        InsertFile(db, library, "seen-late.mkv", lastSeenAt: "2026-10-03 09:00:00");

        var newestFirst = await Paths(db, ProcessingFileSort.LastSeen, SortDirection.Descending);
        var oldestFirst = await Paths(db, ProcessingFileSort.LastSeen, SortDirection.Ascending);

        Assert.Equal(["seen-late.mkv", "seen-early.mkv", "never-seen.mkv"], newestFirst);
        Assert.Equal(["never-seen.mkv", "seen-early.mkv", "seen-late.mkv"], oldestFirst);
    }

    [Theory]
    [MemberData(nameof(EveryOrder))]
    public async Task Paging_in_any_order_visits_every_file_once_and_adds_up_to_the_whole_list(ProcessingFileSort sort, SortDirection direction)
    {
        using var db = new JobsTestDatabase();
        SeedFilesWithManyTies(db);
        var whole = await ListPage(db, sort, direction, after: null, WholeList);

        var walked = new List<long>();
        IReadOnlyList<object?>? after = null;
        var pages = 0;
        do
        {
            var page = await ListPage(db, sort, direction, after, SmallPage);
            walked.AddRange(page.Rows.Select(row => row.Id));
            after = ProcessingFileOrdering.TryDecodeCursor(page.NextCursor, sort, direction, out var next) ? next : null;
            pages++;
        }
        while (after is not null && pages < WholeList);

        Assert.Null(whole.NextCursor);
        Assert.Equal(whole.Rows.Select(row => row.Id), walked);
        Assert.Equal(walked.Count, walked.Distinct().Count());
    }

    [Fact]
    public async Task A_page_that_ends_exactly_on_the_last_file_has_no_cursor()
    {
        using var db = new JobsTestDatabase();
        var library = InsertLibrary(db);
        InsertFile(db, library, "a.mkv");
        InsertFile(db, library, "b.mkv");

        var exactlyAll = await ListPage(db, ProcessingFileSort.File, SortDirection.Ascending, after: null, limit: 2);
        var fewer = await ListPage(db, ProcessingFileSort.File, SortDirection.Ascending, after: null, limit: 1);

        Assert.Null(exactlyAll.NextCursor);
        Assert.NotNull(fewer.NextCursor);
    }

    [Fact]
    public async Task Paging_keeps_to_the_filters_it_started_with()
    {
        using var db = new JobsTestDatabase();
        var movies = InsertLibrary(db, "Movies");
        var shows = InsertLibrary(db, "Shows");
        foreach (var index in Enumerable.Range(0, 7))
        {
            InsertFile(db, movies, $"movie-{index}.mkv", ProcessingFileStatuses.Processed);
            InsertFile(db, shows, $"show-{index}.mkv", ProcessingFileStatuses.Processed);
            InsertFile(db, movies, $"failed-{index}.mkv", ProcessingFileStatuses.ProcessingFailed);
        }

        var seen = new List<string>();
        IReadOnlyList<object?>? after = null;
        do
        {
            var filter = new ProcessingFileListFilter
            {
                LibraryId = movies,
                Statuses = [ProcessingFileStatuses.Processed],
                Sort = ProcessingFileSort.File,
                Direction = SortDirection.Ascending,
                After = after,
                Limit = 3,
            };
            await using var uow = await UnitOfWork.OpenAsync(db.Database);
            var page = await Store.ListPageAsync(uow, filter);
            seen.AddRange(page.Rows.Select(row => row.RelativePath));
            after = ProcessingFileOrdering.TryDecodeCursor(page.NextCursor, ProcessingFileSort.File, SortDirection.Ascending, out var next) ? next : null;
        }
        while (after is not null);

        Assert.Equal(Enumerable.Range(0, 7).Select(index => $"movie-{index}.mkv"), seen);
    }

    [Fact]
    public async Task A_cursor_made_under_one_sort_is_not_a_cursor_under_another()
    {
        using var db = new JobsTestDatabase();
        var library = InsertLibrary(db);
        InsertFile(db, library, "a.mkv");
        InsertFile(db, library, "b.mkv");
        var cursor = (await ListPage(db, ProcessingFileSort.Status, SortDirection.Ascending, after: null, limit: 1)).NextCursor;

        Assert.True(ProcessingFileOrdering.TryDecodeCursor(cursor, ProcessingFileSort.Status, SortDirection.Ascending, out _));
        Assert.False(ProcessingFileOrdering.TryDecodeCursor(cursor, ProcessingFileSort.Status, SortDirection.Descending, out _));
        Assert.False(ProcessingFileOrdering.TryDecodeCursor(cursor, ProcessingFileSort.File, SortDirection.Ascending, out _));
        Assert.False(ProcessingFileOrdering.TryDecodeCursor(cursor, ProcessingFileSort.LastSeen, SortDirection.Ascending, out _));
    }

    [Fact]
    public async Task The_status_ranks_in_the_database_are_the_ranks_the_meanings_give()
    {
        using var db = new JobsTestDatabase();
        var library = InsertLibrary(db);
        foreach (var status in SeededStatuses)
        {
            InsertFile(db, library, $"{status}.mkv", status);
        }

        var page = await ListPage(db, ProcessingFileSort.Status, SortDirection.Ascending, after: null, WholeList);

        Assert.Equal(
            page.Rows.Select(row => ProcessingFileMeanings.RankOf(row.Status)).Order(),
            page.Rows.Select(row => ProcessingFileMeanings.RankOf(row.Status)));
    }

    /// <summary>Files that share a status, a change time and a seen time in several ways, so every order has ties to break.</summary>
    private static void SeedFilesWithManyTies(JobsTestDatabase db)
    {
        var library = InsertLibrary(db);
        for (var index = 0; index < 30; index++)
        {
            var path = index % 5 == 0 ? $"FILE-{index % 7}.mkv" : $"file-{index % 7}.mkv";
            InsertFile(
                db,
                library,
                $"{path[..^4]}-{index}.mkv",
                SeededStatuses[index % 4 * 3 % SeededStatuses.Length],
                updatedAt: $"2026-10-0{1 + (index % 3)} 10:00:00",
                lastSeenAt: index % 6 == 0 ? null : $"2026-10-0{1 + (index % 2)} 09:00:00");
        }
    }

    private static async Task<List<string>> Paths(JobsTestDatabase db, ProcessingFileSort sort, SortDirection direction) =>
        [.. (await ListPage(db, sort, direction, after: null, WholeList)).Rows.Select(row => row.RelativePath)];

    private static async Task<ProcessingFilePage> ListPage(JobsTestDatabase db, ProcessingFileSort sort, SortDirection direction, IReadOnlyList<object?>? after, int limit)
    {
        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        return await Store.ListPageAsync(uow, new ProcessingFileListFilter { Sort = sort, Direction = direction, After = after, Limit = limit });
    }

    private static long InsertLibrary(JobsTestDatabase db, string name = "Movies")
    {
        db.Execute("INSERT INTO libraries (name, media_type) VALUES (@name, 'movie')", ("@name", name));
        return Convert.ToInt64(db.Scalar("SELECT id FROM libraries WHERE name = @name", ("@name", name)));
    }

    private static void InsertFile(
        JobsTestDatabase db,
        long libraryId,
        string relativePath,
        string status = ProcessingFileStatuses.Unprocessed,
        string updatedAt = "2026-10-02 10:00:00",
        string? lastSeenAt = "2026-10-02 10:00:00") =>
        db.Execute(
            "INSERT INTO files (library_id, relative_path, status, last_seen_at, updated_at) VALUES (@lib, @path, @status, @seen, @updated)",
            ("@lib", libraryId), ("@path", relativePath), ("@status", status), ("@seen", lastSeenAt), ("@updated", updatedAt));
}
