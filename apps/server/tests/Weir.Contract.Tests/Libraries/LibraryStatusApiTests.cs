using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Libraries.LibrariesPartAHelpers;
using static Weir.Contract.Tests.Libraries.LibraryViewSeed;

namespace Weir.Contract.Tests.Libraries;

/// <summary>
/// The Library view's status: where each file stands now against the current rules. Exactly one status per file,
/// so the counts add up to the files. Left alone beats cleaning, which beats cant_clean_yet, which beats
/// needs_cleaning, which beats matches. The scan index the fixture seeds holds one file the rules would change,
/// one that matches, one still shared with a download and one Weir cannot read.
/// </summary>
[ContractArea("libraries")]
public sealed class LibraryStatusApiTests(ScannedLibraryFixture fixture) : IClassFixture<ScannedLibraryFixture>
{
    private static readonly string[] Statuses = ["needs_cleaning", "cleaning", "matches", "cant_clean_yet", "left_alone"];

    private static Dictionary<string, string> StatusByPath(JsonObject body) =>
        body["files"]!.AsArray().ToDictionary(file => (string)file!["path"]!, file => (string)file!["status"]!);

    [Fact]
    public async Task Every_file_has_one_status()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["/lib/film.mkv"] = "needs_cleaning",
                ["/lib/show.mkv"] = "matches",
                ["/lib/seeding.mkv"] = "cant_clean_yet",
                ["/lib/broken.mkv"] = "cant_clean_yet",
            },
            StatusByPath(await FilesAsync(admin, fixture.LibraryId)));
    }

    [Fact]
    public async Task The_counts_by_status_add_up_to_the_files()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var totals = (await OverviewAsync(admin, fixture.LibraryId))["totals"]!;

        var byStatus = totals["by_status"]!.AsObject();
        Assert.Equal(
            new Dictionary<string, int>
            {
                ["needs_cleaning"] = 1,
                ["cleaning"] = 0,
                ["matches"] = 1,
                ["cant_clean_yet"] = 2,
                ["left_alone"] = 0,
            },
            byStatus.ToDictionary(pair => pair.Key, pair => (int)pair.Value!));
        Assert.Equal((int)totals["files"]!, byStatus.Sum(pair => (int)pair.Value!));
    }

    [Fact]
    public async Task The_files_listing_carries_the_same_counts_for_the_header_and_for_the_filter()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var body = await FilesAsync(admin, fixture.LibraryId, "status=cant_clean_yet");

        var summary = body["summary"]!;
        Assert.Equal(4, summary["by_status"]!.AsObject().Sum(pair => (int)pair.Value!));
        Assert.Equal(4, (int)summary["files"]!);
        Assert.Equal(2, (int)body["filtered"]!["by_status"]!["cant_clean_yet"]!);
        Assert.Equal(0, (int)body["filtered"]!["by_status"]!["matches"]!);
    }

    [Theory]
    [InlineData("needs_cleaning", new[] { "/lib/film.mkv" })]
    [InlineData("matches", new[] { "/lib/show.mkv" })]
    [InlineData("cant_clean_yet", new[] { "/lib/broken.mkv", "/lib/seeding.mkv" })]
    [InlineData("cleaning", new string[0])]
    [InlineData("left_alone", new string[0])]
    // A status the server does not know narrows nothing.
    [InlineData("nonsense", new[] { "/lib/broken.mkv", "/lib/film.mkv", "/lib/seeding.mkv", "/lib/show.mkv" })]
    public async Task The_files_listing_narrows_to_one_status(string status, string[] expected)
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        Assert.Equal(expected, Paths(await FilesAsync(admin, fixture.LibraryId, $"status={status}")));
    }

    [Fact]
    public async Task A_file_set_aside_is_left_alone_whatever_the_rules_say()
    {
        // Setting a file aside changes what every other read of the library returns, so this test has a server of its own.
        var (server, libraryId) = await StartScannedServerAsync();
        await using var _ = server;
        using var admin = await server.CreateAdminClientAsync();
        var baseUrl = $"{LibrariesUrl}/{libraryId}/library-files/leave-alone";

        var response = await admin.PostWithCsrfAsync(baseUrl, Obj(("path", "/lib/film.mkv"), ("leave_alone", true)));
        response.ShouldBe(HttpStatusCode.OK);

        var body = await FilesAsync(admin, libraryId);
        Assert.Equal("left_alone", StatusByPath(body)["/lib/film.mkv"]);
        Assert.Equal(1, (int)(await OverviewAsync(admin, libraryId))["totals"]!["by_status"]!["left_alone"]!);
        Assert.Equal(["/lib/film.mkv"], Paths(await FilesAsync(admin, libraryId, "status=left_alone")));
    }

    [Fact]
    public async Task A_reason_is_only_given_where_the_scan_can_support_one()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var files = (await FilesAsync(admin, fixture.LibraryId))["files"]!.AsArray();
        var reasons = files.ToDictionary(file => (string)file!["path"]!, file => file!.AsObject());

        // The seeded index was written without a reason, so none is invented for the file that needs cleaning.
        AssertNullField(reasons["/lib/film.mkv"], "status_reason");
        Assert.All(reasons.Values, file => AssertNullField(file, "status_reason"));
    }

    [Fact]
    public async Task The_status_names_are_the_documented_ones()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var names = (await FilesAsync(admin, fixture.LibraryId))["files"]!.AsArray().Select(file => (string)file!["status"]!).ToHashSet();
        Assert.True(names.IsSubsetOf(Statuses), string.Join(", ", names));
    }
}
