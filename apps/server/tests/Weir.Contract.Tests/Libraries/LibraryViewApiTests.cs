using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Libraries.LibrariesPartAHelpers;
using static Weir.Contract.Tests.Libraries.LibraryViewSeed;

namespace Weir.Contract.Tests.Libraries;

/// <summary>The Library view: access, the Overview's totals and breakdowns, and the Problems grouping.</summary>
[ContractArea("libraries")]
public sealed class LibraryViewApiTests(ScannedLibraryFixture fixture) : IClassFixture<ScannedLibraryFixture>
{
    private static Dictionary<string, int> FileCounts(JsonNode? rows) =>
        rows!.AsArray().ToDictionary(row => (string)row!["value"]!, row => (int)row!["files"]!);

    [Fact]
    public async Task The_library_view_endpoints_need_a_signed_in_user()
    {
        using var anonymous = fixture.Server.CreateClient();
        foreach (var suffix in new[] { "library-overview", "library-problems", "library-files" })
        {
            (await anonymous.GetAsync($"{LibrariesUrl}/{fixture.LibraryId}/{suffix}")).ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task A_library_that_does_not_exist_is_a_404()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var missing = fixture.LibraryId + 9_000;
        foreach (var suffix in new[] { "library-overview", "library-problems" })
        {
            (await admin.GetAsync($"{LibrariesUrl}/{missing}/{suffix}")).ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task The_overview_totals_count_every_scanned_file_and_its_size()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var totals = (await OverviewAsync(admin, fixture.LibraryId))["totals"]!;

        Assert.Equal(4, (int)totals["files"]!);
        Assert.Equal(6_500, (long)totals["size_bytes"]!);
        Assert.Equal(1, (int)totals["matches"]!);
        Assert.Equal(2, (int)totals["would_change"]!);
        Assert.Equal(1, (int)totals["cannot_process"]!);
    }

    [Fact]
    public async Task Every_breakdown_is_returned_with_counts_and_shares()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var breakdowns = (await OverviewAsync(admin, fixture.LibraryId))["breakdowns"]!.AsObject();

        Assert.Equal(
            new[] { "video_codec", "resolution", "audio", "audio_language", "subtitle_language" }.Order(),
            breakdowns.Select(pair => pair.Key).Order());

        var codecRows = breakdowns["video_codec"]!.AsArray();
        var codecs = codecRows.ToDictionary(row => (string)row!["value"]!, row => row!);
        Assert.Equal(2, (int)codecs["h264"]["files"]!);
        Assert.Equal(1, (int)codecs["hevc"]["files"]!);
        Assert.Equal(1, (int)codecs["unknown"]["files"]!);
        Assert.Equal(0.5, (double)codecs["h264"]["share"]!, 6);
        // Biggest group first, so the bars read top-down.
        Assert.Equal("h264", (string)codecRows[0]!["value"]!);

        // A file with two English audio tracks still counts once.
        Assert.Equal(new Dictionary<string, int> { ["eng"] = 3, ["jpn"] = 1 }, FileCounts(breakdowns["audio_language"]));

        Assert.Equal(
            new Dictionary<string, int> { ["ac3 5.1"] = 2, ["aac stereo"] = 1, ["eac3 5.1"] = 1 },
            FileCounts(breakdowns["audio"]));
    }

    [Fact]
    public async Task Problems_are_grouped_by_reason_with_something_to_do_about_each()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var response = await admin.GetAsync($"{LibrariesUrl}/{fixture.LibraryId}/library-problems");
        response.ShouldBe(HttpStatusCode.OK);
        var body = response.Fields;

        var groups = body["groups"]!.AsArray().ToDictionary(group => (string)group!["kind"]!, group => group!);
        Assert.Equal(new[] { "seeding", "unreadable" }.Order(), groups.Keys.Order());
        Assert.Equal(2, (int)body["total"]!);
        Assert.Equal(1, (int)groups["seeding"]["files"]!);
        Assert.Equal(["/lib/seeding.mkv"], StringList(groups["seeding"]["sample_paths"]));
        Assert.False(string.IsNullOrEmpty((string?)groups["seeding"]["what_to_do"]));
        Assert.False(string.IsNullOrEmpty((string?)groups["unreadable"]["title"]));

        // The same groups reach the Overview, so its "N files need a look" line agrees with this view.
        var overviewKinds = (await OverviewAsync(admin, fixture.LibraryId))["problems"]!.AsArray().Select(group => (string)group!["kind"]!);
        Assert.Equal(groups.Keys.ToHashSet(), overviewKinds.ToHashSet());
    }

    [Fact]
    public async Task Allowing_hardlinked_files_stops_seeding_being_reported_as_a_problem()
    {
        // Saving the setting changes what the library reports for good, so this test has a server of its own.
        var (server, libraryId) = await StartScannedServerAsync();
        await using var _ = server;
        using var admin = await server.CreateAdminClientAsync();
        var settingsUrl = $"{LibrariesUrl}/{libraryId}/library-settings";

        var settings = await admin.GetAsync(settingsUrl);
        var saved = await admin.PutWithCsrfAsync(
            settingsUrl,
            Obj(("library_folders", settings.Fields["library_folders"]!.DeepClone()), ("clean_hardlinked_files", true)));
        saved.ShouldBe(HttpStatusCode.OK);

        var problems = await admin.GetAsync($"{LibrariesUrl}/{libraryId}/library-problems");
        var kinds = problems.Fields["groups"]!.AsArray().Select(group => (string)group!["kind"]!);
        Assert.Equal(new[] { "unreadable" }, kinds.ToHashSet());
    }

    [Fact]
    public async Task An_unscanned_library_answers_with_empty_totals()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var created = await admin.PostWithCsrfAsync(
            LibrariesUrl,
            Obj(
                ("enabled", false),
                ("name", "Never scanned"),
                ("media_type", "movie"),
                ("watched_folder", "/srv/never/in"),
                ("output_folder", "/srv/never/out")));
        created.ShouldBe(HttpStatusCode.Created);
        var libraryId = (long)created.Fields["id"]!;

        var body = await OverviewAsync(admin, libraryId);

        Assert.Equal(0, (int)body["totals"]!["files"]!);
        Assert.Equal(0, (int)body["folders_configured"]!);
        AssertNullField(body, "scan");
        Assert.Empty(body["breakdowns"]!["video_codec"]!.AsArray());
        Assert.Empty(body["problems"]!.AsArray());
        Assert.Empty((await FilesAsync(admin, libraryId))["files"]!.AsArray());
    }

    [Fact]
    public async Task The_new_views_are_in_the_published_openapi_document()
    {
        using var client = fixture.Server.CreateClient();
        var response = await client.GetAsync("/openapi.json");
        var documented = response.Fields["paths"]!.AsObject();

        foreach (var path in new[]
        {
            "/api/v1/processing/libraries/{library_id}/library-overview",
            "/api/v1/processing/libraries/{library_id}/library-problems",
        })
        {
            Assert.True(documented.ContainsKey(path), $"{path} is not in the served OpenAPI document");
            Assert.True(documented[path]!.AsObject().ContainsKey("get"), $"{path} has no GET");
        }
    }
}
