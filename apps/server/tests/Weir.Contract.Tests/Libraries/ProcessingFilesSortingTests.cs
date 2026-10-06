using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Libraries.FilesSortModel;
using static Weir.Contract.Tests.Libraries.LibrariesPartAHelpers;

namespace Weir.Contract.Tests.Libraries;

/// <summary>The Files list in each order it can be asked for: sorted by path, status or time, in either direction, paged by cursor.</summary>
[ContractArea("libraries")]
public sealed class ProcessingFilesSortingTests(ProcessingFilesSortingTests.SortingFixture fixture)
    : IClassFixture<ProcessingFilesSortingTests.SortingFixture>
{
    private const string Files = Api + "/processing/files";
    private const int FileCount = 36;
    private const int SmallPage = 5;
    private const int WholeList = 1000;

    private static readonly string[] Statuses =
    [
        "unprocessed",
        "processing",
        "processed",
        "processing_failed",
        "skipped",
        "disabled",
        "on_hold",
        "out_of_schedule",
        "blocked_upstream",
        "passed_through",
        "rejected",
        "cancelled",
    ];

    // The cleaned copies Weir handed back, by the file's position in the seeded order. All of the files are in a workflow linked
    // to a media manager, so a copy that no manager has answered about and Weir has not settled is still waiting for one.
    private static readonly Dictionary<int, (string Column, object? Value)[]> HandbacksByIndex = new()
    {
        [2] = [],
        [14] = [("outcome", "imported"), ("outcome_by", "Radarr")],
        [26] = [("settled_at", "2026-10-01 12:00:00"), ("release_note", "Weir removed its copy.")],
        [5] = [("outcome", "not-imported"), ("outcome_by", "Radarr")],
    };

    private static readonly int[] WaitingIndexes = [2];

    private static readonly string[] ChangedAt =
        ["2026-09-01 08:00:00", "2026-09-01 09:30:00", "2026-09-02 10:00:00", "2026-09-03 11:15:00"];

    private static readonly string[] SeenAt = ["2026-10-01 06:00:00", "2026-10-01 07:00:00", "2026-10-02 08:00:00"];

    public static IEnumerable<object?[]> Orders() =>
        from sort in new string?[] { "file", "status", "when", null }
        from direction in new[] { "asc", "desc" }
        select new object?[] { sort, direction };

    // --- reading a list ---------------------------------------------------------------------------

    private static (string, object?)[] Query(string? sort, string? direction) =>
        [("sort", sort), ("direction", direction)];

    private static async Task<JsonObject> GetAsync(WeirClient client, params (string Name, object? Value)[] parameters)
    {
        var query = new Dictionary<string, object?> { ["limit"] = WholeList };
        foreach (var (name, value) in parameters)
        {
            query[name] = value;
        }

        var response = await LibrariesPartAHelpers.GetAsync(client, Files, query.Select(pair => (pair.Key, pair.Value)).ToArray());
        response.ShouldBe(HttpStatusCode.OK);
        return response.Fields;
    }

    private static List<JsonObject> Rows(JsonObject body) => Objects(body["files"]);

    private static List<long> Ids(JsonObject body) => Rows(body).Select(Id).ToList();

    private static async Task<List<long>> WalkAsync(
        WeirClient client, (string, object?)[] order, int limit, params (string Name, object? Value)[] filters)
    {
        var walked = new List<long>();
        string? cursor = null;
        for (var attempt = 0; attempt < FileCount; attempt++)
        {
            var page = await GetAsync(client, [.. order, .. filters, ("limit", limit), ("cursor", cursor)]);
            walked.AddRange(Ids(page));
            cursor = (string?)page["next_cursor"];
            if (cursor is null)
            {
                break;
            }
        }

        Assert.Null(cursor);
        return walked;
    }

    // --- the tests --------------------------------------------------------------------------------

    [Fact]
    public async Task The_seeded_files_tie_on_every_sort()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var rows = Rows(await GetAsync(admin));

        Assert.Equal(FileCount, rows.Count);
        Assert.Equal(Statuses.ToHashSet(), rows.Select(StatusOf).ToHashSet());
        Assert.Equal(FileCount / 2, rows.Select(row => ((string)row["relative_path"]!).ToLowerInvariant()).Distinct().Count());
        Assert.True(rows.Select(row => (string)row["updated_at"]!).Distinct().Count() < FileCount / 2);
        Assert.Contains(rows, row => row["last_seen_at"] is null);
        Assert.Equal(WaitingIndexes.Length, rows.Count(AwaitsImport));
        Assert.Equal(HandbacksByIndex.Count, rows.Count(row => row["handback"] is not null));
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public async Task Files_come_in_the_order_the_sort_and_direction_ask_for(string? sort, string direction)
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var body = await GetAsync(admin, Query(sort, direction));

        Assert.Equal(Expected(Rows(body), sort, direction), Ids(body));
        AssertNullField(body, "next_cursor");
    }

    [Fact]
    public async Task A_list_asked_for_no_sort_is_newest_seen_first_and_files_never_seen_come_last()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var seen = Rows(await GetAsync(admin)).Select(row => Time(row["last_seen_at"])).ToList();

        var firstUnseen = seen.IndexOf(null);
        Assert.True(firstUnseen >= 0, "no file was never seen");
        Assert.All(seen.Skip(firstUnseen), moment => Assert.Null(moment));
        var seenOnes = seen.Take(firstUnseen).ToList();
        Assert.Equal(seenOnes.OrderByDescending(moment => moment), seenOnes);
    }

    [Fact]
    public async Task Sorting_by_status_follows_what_the_status_means_and_then_the_status_word()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var rows = Rows(await GetAsync(admin, ("sort", "status"), ("direction", "asc"))).Where(row => !AwaitsImport(row)).ToList();

        var distinct = rows.Select(StatusOf).Distinct().ToList();
        Assert.Equal(StatusesByMeaning, distinct);
        var descending = Rows(await GetAsync(admin, ("sort", "status"), ("direction", "desc"))).Where(row => !AwaitsImport(row));
        Assert.Equal(Enumerable.Reverse(rows).Select(StatusOf), descending.Select(StatusOf));
    }

    [Fact]
    public async Task A_cleaned_copy_waiting_for_its_media_manager_sorts_with_the_to_do_files_and_still_reads_processed()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var rows = Rows(await GetAsync(admin, ("sort", "status"), ("direction", "asc")));

        var waiting = rows.Where(AwaitsImport).ToList();
        var positions = rows.Select((row, position) => (Id: Id(row), Position: position)).ToDictionary(item => item.Id, item => item.Position);
        var finished = rows.Where(row => StatusOf(row) == "processed" && !AwaitsImport(row)).Select(row => positions[Id(row)]).ToList();
        var inProgress = rows.Where(row => StatusOf(row) == "processing").Select(row => positions[Id(row)]).ToList();
        var toDoStatuses = rows.Where(row => Rank(row) == ToDo).Select(StatusOf).Distinct().ToList();
        Assert.Equal(WaitingIndexes.Length, waiting.Count);
        Assert.Equal(new HashSet<string> { "processed" }, waiting.Select(StatusOf).ToHashSet());
        var firstWaiting = waiting.Min(row => positions[Id(row)]);
        Assert.True(finished.Max() < firstWaiting && firstWaiting < inProgress.Min());
        Assert.Equal(["blocked_upstream", "out_of_schedule", "processed", "unprocessed"], toDoStatuses);
    }

    [Fact]
    public async Task A_cleaned_copy_a_media_manager_answered_or_weir_settled_stays_with_the_finished_files()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var rows = Rows(await GetAsync(admin, ("sort", "status"), ("direction", "asc")));

        var positions = rows.Select((row, position) => (Id: Id(row), Position: position)).ToDictionary(item => item.Id, item => item.Position);
        var answered = rows.Where(row => row["handback"] is not null && !AwaitsImport(row)).ToList();
        var finished = answered.Where(row => StatusOf(row) == "processed").Select(row => positions[Id(row)]).ToList();
        var firstToDo = rows.Where(row => StatusOf(row) == "blocked_upstream").Min(row => positions[Id(row)]);
        Assert.Equal(HandbacksByIndex.Count - WaitingIndexes.Length, answered.Count);
        Assert.Equal(2, finished.Count);
        Assert.True(finished.Max() < firstToDo);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("status")]
    [InlineData("when")]
    public async Task Files_that_tie_on_the_sort_value_fall_by_id_in_the_direction_of_the_sort(string sort)
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        foreach (var direction in new[] { "asc", "desc" })
        {
            var rows = Rows(await GetAsync(admin, ("sort", sort), ("direction", direction)));
            var ties = new Dictionary<string, List<long>>();
            foreach (var row in rows)
            {
                var value = sort switch
                {
                    "file" => ((string)row["relative_path"]!).ToLowerInvariant(),
                    "status" => $"{Rank(row)}/{StatusOf(row)}",
                    _ => (string)row["updated_at"]!,
                };
                ties.TryAdd(value, []);
                ties[value].Add(Id(row));
            }

            Assert.Contains(ties.Values, ids => ids.Count > 1);
            Assert.All(
                ties.Values,
                ids => Assert.Equal(direction == "desc" ? ids.OrderByDescending(id => id) : ids.Order(), ids));
        }
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public async Task Paging_in_any_order_visits_every_file_once_and_adds_up_to_the_whole_list(string? sort, string direction)
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var order = Query(sort, direction);
        var whole = await GetAsync(admin, order);
        var walked = new List<long>();
        string? cursor = null;
        var pages = 0;
        for (var attempt = 0; attempt < FileCount; attempt++)
        {
            var page = await GetAsync(admin, [.. order, ("limit", SmallPage), ("cursor", cursor)]);
            walked.AddRange(Ids(page));
            Assert.Equal(SmallPage, (int)page["limit"]!);
            Assert.Equal(page["files"]!.AsArray().Count, (int)page["returned"]!);
            Assert.True(JsonNode.DeepEquals(whole["status_counts"], page["status_counts"]), "status_counts differ between pages");
            cursor = (string?)page["next_cursor"];
            pages++;
            if (cursor is null)
            {
                break;
            }
        }

        Assert.Null(cursor);
        Assert.Equal((FileCount + SmallPage - 1) / SmallPage, pages);
        Assert.Equal(Ids(whole), walked);
        Assert.Equal(FileCount, walked.Distinct().Count());
    }

    [Theory]
    [InlineData("file")]
    [InlineData("status")]
    [InlineData("when")]
    public async Task A_page_that_ends_exactly_on_the_last_file_has_no_cursor(string sort)
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var exact = await GetAsync(admin, ("sort", sort), ("limit", FileCount));
        var oneShort = await GetAsync(admin, ("sort", sort), ("limit", FileCount - 1));
        var last = await GetAsync(admin, ("sort", sort), ("limit", FileCount - 1), ("cursor", (string?)oneShort["next_cursor"]));

        AssertNullField(exact, "next_cursor");
        Assert.NotNull(oneShort["next_cursor"]);
        Assert.Equal(1, (int)last["returned"]!);
        AssertNullField(last, "next_cursor");
        Assert.Equal(Ids(exact), Ids(oneShort).Concat(Ids(last)));
    }

    [Theory]
    [InlineData("file")]
    [InlineData("status")]
    [InlineData("when")]
    public async Task A_filter_and_a_sort_page_together(string sort)
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        (string, object?)[] filters = [("file_status", "processing_failed,rejected,processed"), ("path_contains", "title 0")];
        var whole = Ids(await GetAsync(admin, [("sort", sort), ("direction", "asc"), .. filters]));

        var walked = await WalkAsync(admin, [("sort", sort), ("direction", "asc")], 2, filters);

        Assert.Equal(whole, walked);
        Assert.True(whole.Count > 2 && whole.Count < FileCount, $"{whole.Count} files matched");
    }

    [Theory]
    [InlineData("name", null)]
    [InlineData("last_seen", null)]
    [InlineData("File", null)]
    [InlineData("", null)]
    [InlineData(null, "up")]
    [InlineData(null, "ASC")]
    [InlineData(null, "")]
    [InlineData("file", "sideways")]
    public async Task A_sort_or_direction_the_list_does_not_have_is_refused(string? sort, string? direction)
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var response = await LibrariesPartAHelpers.GetAsync(admin, Files, Query(sort, direction));

        response.ShouldBe(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("query", (string)response.Fields["detail"]![0]!["loc"]![0]!);
    }

    [Theory]
    [InlineData("file", "desc")]
    [InlineData("status", "asc")]
    [InlineData("when", "asc")]
    [InlineData(null, "asc")]
    [InlineData(null, null)]
    public async Task A_cursor_made_for_another_sort_or_direction_is_refused(string? sort, string? direction)
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var cursor = (string?)(await GetAsync(admin, ("sort", "file"), ("direction", "asc"), ("limit", SmallPage)))["next_cursor"];

        var response = await LibrariesPartAHelpers.GetAsync(admin, Files, [.. Query(sort, direction), ("cursor", cursor)]);

        response.ShouldBe(HttpStatusCode.UnprocessableEntity);
        Assert.Equal(["query", "cursor"], StringList(response.Fields["detail"]![0]!["loc"]));
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("e30")]
    [InlineData("W10")]
    [InlineData("eyJzb3J0IjoiZmlsZSJ9")]
    [InlineData("AAAA")]
    [InlineData("!!")]
    public async Task A_cursor_the_list_did_not_give_out_is_refused_under_every_sort(string cursor)
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        foreach (var sort in new string?[] { "file", "status", "when", null })
        {
            var response = await LibrariesPartAHelpers.GetAsync(admin, Files, [.. Query(sort, "asc"), ("cursor", cursor)]);

            Assert.True(response.Status == HttpStatusCode.UnprocessableEntity, $"{sort}: {response}");
        }
    }

    /// <summary>
    /// Files that tie on every sort: pairs of paths that differ only in case, a handful of change times shared by many
    /// files, every status, and some files no scan has seen.
    /// </summary>
    public sealed class SortingFixture : SeededServerFixture
    {
        protected override void Seed(SqliteConnection connection)
        {
            var libraryId = FirstLibraryId(connection);
            LinkLibraryToManager(connection, libraryId);
            for (var index = 0; index < FileCount; index++)
            {
                var path = $"Title {index / 2:D2}/Episode.mkv";
                var relativePath = index % 2 == 1 ? path.ToLowerInvariant() : path;
                InsertFile(
                    connection,
                    libraryId,
                    relativePath,
                    ("status", Statuses[index % Statuses.Length]),
                    ("updated_at", ChangedAt[index % ChangedAt.Length]),
                    ("last_seen_at", index % 5 == 0 ? null : SeenAt[index % SeenAt.Length]));
                if (HandbacksByIndex.TryGetValue(index, out var columns))
                {
                    InsertHandback(connection, libraryId, relativePath, columns);
                }
            }
        }
    }
}
