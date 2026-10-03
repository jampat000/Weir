using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Libraries.LibrariesPartAHelpers;
using static Weir.Contract.Tests.Libraries.LibraryViewSeed;

namespace Weir.Contract.Tests.Libraries;

/// <summary>The Library view's Files table: per-file media facts, facet filters, sorting and paging.</summary>
[ContractArea("libraries")]
public sealed class LibraryViewFilesApiTests(ScannedLibraryFixture fixture) : IClassFixture<ScannedLibraryFixture>
{
    private static readonly string[] AllPathsByName = ["/lib/broken.mkv", "/lib/film.mkv", "/lib/seeding.mkv", "/lib/show.mkv"];

    private async Task<System.Text.Json.Nodes.JsonObject> FileAsync(string path)
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var files = (await FilesAsync(admin, fixture.LibraryId))["files"]!.AsArray();
        return files.Single(file => (string)file!["path"]! == path)!.AsObject();
    }

    [Fact]
    public async Task A_file_reports_the_media_facts_the_table_shows()
    {
        var film = await FileAsync("/lib/film.mkv");

        Assert.Equal("hevc", (string)film["video_codec"]!);
        Assert.Equal("4k", (string)film["resolution_class"]!);
        Assert.Equal(2160, (int)film["video_height"]!);
        Assert.Equal(2, (int)film["audio_track_count"]!);
        Assert.Equal("eng eac3 5.1, jpn aac stereo", (string)film["audio_summary"]!);
        Assert.Equal("eng", (string)film["subtitle_summary"]!);
        Assert.Equal("Blade Runner 2049", (string)film["manager_title"]!);
    }

    [Fact]
    public async Task A_file_with_no_cached_probe_reads_unknown()
    {
        var broken = await FileAsync("/lib/broken.mkv");

        Assert.Equal("unknown", (string)broken["video_codec"]!);
        Assert.Equal("unknown", (string)broken["resolution_class"]!);
        AssertNullField(broken, "audio_summary");
        Assert.Equal("unreadable", (string)broken["problem_kind"]!);
    }

    [Theory]
    [InlineData("audio_language=jpn", new[] { "/lib/film.mkv" })]
    [InlineData("resolution=1080p", new[] { "/lib/seeding.mkv", "/lib/show.mkv" })]
    [InlineData("video_codec=h264&resolution=1080p", new[] { "/lib/seeding.mkv", "/lib/show.mkv" })]
    [InlineData("video_codec=hevc&resolution=1080p", new string[0])]
    [InlineData("subtitle_language=eng", new[] { "/lib/film.mkv" })]
    [InlineData("audio=ac3 5.1", new[] { "/lib/seeding.mkv", "/lib/show.mkv" })]
    [InlineData("classification=would_change", new[] { "/lib/film.mkv", "/lib/seeding.mkv" })]
    [InlineData("problem=seeding", new[] { "/lib/seeding.mkv" })]
    [InlineData("problem=unreadable", new[] { "/lib/broken.mkv" })]
    [InlineData("manager=radarr", new[] { "/lib/film.mkv" })]
    [InlineData("q=runner", new[] { "/lib/film.mkv" })]
    [InlineData("q=seed", new[] { "/lib/seeding.mkv" })]
    // An unknown facet name is ignored rather than narrowing (or reaching) the query.
    [InlineData("not_a_facet=hevc", new[] { "/lib/broken.mkv", "/lib/film.mkv", "/lib/seeding.mkv", "/lib/show.mkv" })]
    public async Task A_facet_filter_narrows_the_files_listing(string query, string[] expected)
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        Assert.Equal(expected, Paths(await FilesAsync(admin, fixture.LibraryId, query)));
    }

    [Fact]
    public async Task A_filter_narrows_the_filtered_totals_but_not_the_header_summary()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var body = await FilesAsync(admin, fixture.LibraryId, "audio_language=jpn");

        Assert.Equal(1, (int)body["total"]!);
        Assert.Equal(1, (int)body["filtered"]!["files"]!);
        Assert.Equal(3_000, (long)body["filtered"]!["size_bytes"]!);
        Assert.Equal(4, (int)body["summary"]!["files"]!);
        Assert.Equal(6_500, (long)body["summary"]!["size_bytes"]!);
    }

    [Fact]
    public async Task Files_are_sorted_by_the_named_column_in_both_directions()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var ascending = await FilesAsync(admin, fixture.LibraryId, "sort=size&direction=asc");
        var descending = await FilesAsync(admin, fixture.LibraryId, "sort=size&direction=desc");

        Assert.Equal("size", (string)ascending["sort"]!);
        Assert.Equal("asc", (string)ascending["direction"]!);
        Assert.Equal(["/lib/broken.mkv", "/lib/show.mkv", "/lib/seeding.mkv", "/lib/film.mkv"], Paths(ascending));
        Assert.Equal(Enumerable.Reverse(Paths(ascending)), Paths(descending));
    }

    [Fact]
    public async Task An_unknown_sort_falls_back_to_the_path_rather_than_failing()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var body = await FilesAsync(admin, fixture.LibraryId, "sort=size_bytes;DROP+TABLE+library_files");

        Assert.Equal("path", (string)body["sort"]!);
        Assert.Equal(AllPathsByName, Paths(body));
        // Proof the rejected text never reached the database: the rows are all still there.
        Assert.Equal(4, (int)body["summary"]!["files"]!);
    }

    [Fact]
    public async Task Paging_returns_every_row_exactly_once()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var first = await FilesAsync(admin, fixture.LibraryId, "page_size=3");
        var second = await FilesAsync(admin, fixture.LibraryId, "page_size=3&page=2");

        Assert.Equal(1, (int)first["page"]!);
        Assert.Equal(3, (int)first["page_size"]!);
        Assert.Equal(4, (int)first["total"]!);
        Assert.Equal(3, first["files"]!.AsArray().Count);
        Assert.Equal(["/lib/show.mkv"], Paths(second));
        Assert.Equal(AllPathsByName, Paths(first).Concat(Paths(second)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_page_size_beyond_the_cap_is_clamped()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var body = await FilesAsync(admin, fixture.LibraryId, "page_size=100000&page=0");

        Assert.Equal(200, (int)body["page_size"]!);
        Assert.Equal(1, (int)body["page"]!);
    }
}
