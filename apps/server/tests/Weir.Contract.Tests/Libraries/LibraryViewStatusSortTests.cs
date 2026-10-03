using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Libraries.LibrariesPartAHelpers;
using static Weir.Contract.Tests.Libraries.LibraryViewSeed;

namespace Weir.Contract.Tests.Libraries;

/// <summary>The Library view's Files table sorted by status: where each file stands now, in the order the statuses read.</summary>
[ContractArea("libraries")]
public sealed class LibraryViewStatusSortTests(LibraryViewStatusSortTests.StatusSortFixture fixture)
    : IClassFixture<LibraryViewStatusSortTests.StatusSortFixture>
{
    private const int SmallPage = 2;

    // Matches, needs cleaning, cleaning, can't clean yet, can't clean yet because Weir cannot read or open the file, left alone.
    // Files of one rank fall by path, whichever way the sort runs.
    private static readonly string[] Ascending =
    [
        "/lib/also-ok.mkv",
        "/lib/show.mkv",
        "/lib/film.mkv",
        "/lib/cleaning.mkv",
        "/lib/no-video.mkv",
        "/lib/seeding.mkv",
        "/lib/broken.mkv",
        "/lib/no-permission.mkv",
        "/lib/left-alone.mkv",
    ];

    private static readonly string[] Descending =
    [
        "/lib/left-alone.mkv",
        "/lib/broken.mkv",
        "/lib/no-permission.mkv",
        "/lib/no-video.mkv",
        "/lib/seeding.mkv",
        "/lib/cleaning.mkv",
        "/lib/film.mkv",
        "/lib/also-ok.mkv",
        "/lib/show.mkv",
    ];

    private static readonly Dictionary<string, string> StatusOf = new()
    {
        ["/lib/also-ok.mkv"] = "matches",
        ["/lib/show.mkv"] = "matches",
        ["/lib/film.mkv"] = "needs_cleaning",
        ["/lib/cleaning.mkv"] = "cleaning",
        ["/lib/no-video.mkv"] = "cant_clean_yet",
        ["/lib/seeding.mkv"] = "cant_clean_yet",
        ["/lib/broken.mkv"] = "cant_clean_yet",
        ["/lib/no-permission.mkv"] = "cant_clean_yet",
        ["/lib/left-alone.mkv"] = "left_alone",
    };

    private static string[] Expected(string direction) => direction == "asc" ? Ascending : Descending;

    [Fact]
    public async Task The_library_holds_a_file_in_every_status()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var files = (await FilesAsync(admin, fixture.LibraryId))["files"]!.AsArray();

        Assert.Equal(StatusOf, files.ToDictionary(file => (string)file!["path"]!, file => (string)file!["status"]!));
        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["/lib/no-video.mkv"] = "no_video",
                ["/lib/seeding.mkv"] = null,
                ["/lib/broken.mkv"] = "unreadable",
                ["/lib/no-permission.mkv"] = "no_permission",
            },
            files.Where(file => (string)file!["status"]! == "cant_clean_yet")
                .ToDictionary(file => (string)file!["path"]!, file => (string?)file!["problem_kind"]));
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    public async Task Files_sorted_by_status_follow_the_order_the_statuses_read(string direction)
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var body = await FilesAsync(admin, fixture.LibraryId, $"sort=status&direction={direction}");

        Assert.Equal("status", (string)body["sort"]!);
        Assert.Equal(direction, (string)body["direction"]!);
        Assert.Equal(Expected(direction), Paths(body));
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    public async Task Paging_by_status_returns_every_file_once(string direction)
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var walked = new List<string>();
        var pages = (StatusOf.Count + SmallPage - 1) / SmallPage;
        for (var page = 1; page <= pages; page++)
        {
            walked.AddRange(Paths(await FilesAsync(
                admin, fixture.LibraryId, $"sort=status&direction={direction}&page_size={SmallPage}&page={page}")));
        }

        Assert.Equal(Expected(direction), walked);
        var beyond = await FilesAsync(admin, fixture.LibraryId, $"sort=status&page_size={SmallPage}&page={pages + 1}");
        Assert.Empty(beyond["files"]!.AsArray());
    }

    [Fact]
    public async Task A_status_filter_and_the_status_sort_work_together()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var body = await FilesAsync(admin, fixture.LibraryId, "sort=status&status=cant_clean_yet");

        Assert.Equal(
            ["/lib/no-video.mkv", "/lib/seeding.mkv", "/lib/broken.mkv", "/lib/no-permission.mkv"],
            Paths(body));
    }

    [Fact]
    public async Task A_sort_that_is_not_listed_still_falls_back_to_the_path()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var body = await FilesAsync(admin, fixture.LibraryId, "sort=statuses");

        Assert.Equal("path", (string)body["sort"]!);
        Assert.Equal(StatusOf.Keys.Order(StringComparer.Ordinal), Paths(body));
    }

    /// <summary>The four-file scan index of the Library view tests, plus one file in each status it leaves out and some that tie.</summary>
    public sealed class StatusSortFixture : ScannedLibraryFixture
    {
        protected override void Seed(SqliteConnection connection)
        {
            base.Seed(connection);
            var libraryId = LibraryId;
            InsertLibraryFile(connection, libraryId, "/lib/also-ok.mkv", "matches", ShowProbe);
            InsertLibraryFile(connection, libraryId, "/lib/cleaning.mkv", "would_change", FilmProbe);
            InsertLibraryFile(connection, libraryId, "/lib/no-permission.mkv", "cannot_process", string.Empty, problemKind: "no_permission");
            InsertLibraryFile(connection, libraryId, "/lib/no-video.mkv", "cannot_process", string.Empty, problemKind: "no_video");
            InsertLibraryFile(connection, libraryId, "/lib/left-alone.mkv", "would_change", FilmProbe);
            InsertJob(
                connection,
                $"processing.library.clean.v1:{libraryId}:contract-cleaning",
                "processing.library.clean.v1",
                "pending",
                new JsonObject { ["path"] = "/lib/cleaning.mkv" });
            SeedSql.Execute(
                connection,
                "INSERT INTO library_file_marks (library_id, path, leave_alone) VALUES ($library, '/lib/left-alone.mkv', 1)",
                ("$library", libraryId));
        }
    }
}
