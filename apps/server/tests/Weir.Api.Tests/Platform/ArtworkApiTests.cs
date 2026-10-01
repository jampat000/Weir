using System.Net;
using Weir.Core.Artwork;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.Platform;

/// <summary>Posters over HTTP: the image route, the poster address on every file-shaped item, and the Artwork switch.</summary>
public sealed class ArtworkApiTests
{
    private const string PosterFile = "zv7J85D8CC9qYagAEhPM63CIG6j.jpg";
    private const string FilePath = "Nosferatu (1922)/Nosferatu.1922.mkv";
    private static readonly byte[] ImageBytes = [0xFF, 0xD8, 0xFF, 0xE0, 9, 8, 7];
    private static readonly string PosterId = ArtworkKeys.PosterId(PosterFile);

    private static async Task<(WeirTestServer Server, ApiTestClient Client)> StartSignedInAsync()
    {
        var server = await StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        return (server, client);
    }

    private static Task<long> SeedLibraryAsync(WeirTestServer server) =>
        TestDatabase.ScalarAsync(server, "SELECT id FROM libraries WHERE media_type = 'movie' ORDER BY id LIMIT 1");

    /// <summary>A file with a stored poster: the title's lookup, the image row, the image on disk and the file's link to its title.</summary>
    private static async Task<long> SeedFileWithPosterAsync(WeirTestServer server, bool imageOnDisk = true)
    {
        var library = await SeedLibraryAsync(server);
        await TestDatabase.ExecuteAsync(server, "INSERT INTO files (library_id, relative_path, status, last_seen_at) VALUES ($l, $p, 'processed', CURRENT_TIMESTAMP)", ("$l", library), ("$p", FilePath));
        await TestDatabase.ExecuteAsync(server, "INSERT INTO artwork_posters (poster_id, source_ref, content_type, size_bytes) VALUES ($id, $ref, 'image/jpeg', $size)", ("$id", PosterId), ("$ref", PosterFile), ("$size", ImageBytes.Length));
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO artwork_lookups (lookup_key, media_scope, title, year, outcome, poster_id) VALUES ('k', 'movie', 'nosferatu', 1922, 'found', $id)",
            ("$id", PosterId));
        await TestDatabase.ExecuteAsync(server, "INSERT INTO artwork_files (library_id, relative_path, lookup_key) VALUES ($l, $p, 'k')", ("$l", library), ("$p", FilePath));
        if (imageOnDisk)
        {
            var directory = Path.Join(server.Home, "artwork", "posters");
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Join(directory, PosterId), ImageBytes);
        }

        return library;
    }

    [Fact]
    public async Task A_poster_is_served_with_its_image_type_and_a_long_private_cache()
    {
        var (server, client) = await StartSignedInAsync();
        await using var _ = server;
        await SeedFileWithPosterAsync(server);

        using var response = await client.GetAsync($"/api/v1/artwork/posters/{PosterId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", Header(response, "Content-Type"));
        var cache = response.Headers.CacheControl!;
        Assert.True(cache.Private);
        Assert.Equal(TimeSpan.FromDays(30), cache.MaxAge);
        Assert.Contains("immutable", Header(response, "Cache-Control"), StringComparison.Ordinal);
        Assert.False(cache.NoStore);
        Assert.Equal(ImageBytes, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task A_poster_needs_a_signed_in_user()
    {
        await using var server = await StartServerAsync();
        await SeedFileWithPosterAsync(server);

        using var response = await new ApiTestClient(server).GetAsync($"/api/v1/artwork/posters/{PosterId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("not-a-poster-id")]
    [InlineData("..%2F..%2Fdata%2Fweir.sqlite3")]
    public async Task An_id_Weir_does_not_hold_is_a_404(string id)
    {
        var (server, client) = await StartSignedInAsync();
        await using var _ = server;

        using var response = await client.GetAsync($"/api/v1/artwork/posters/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Weir has no poster with that id.", await Detail(response));
    }

    [Fact]
    public async Task A_poster_whose_image_is_gone_from_disk_is_a_404()
    {
        var (server, client) = await StartSignedInAsync();
        await using var _ = server;
        await SeedFileWithPosterAsync(server, imageOnDisk: false);

        using var response = await client.GetAsync($"/api/v1/artwork/posters/{PosterId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_file_with_a_poster_carries_its_address_and_one_without_carries_null()
    {
        var (server, client) = await StartSignedInAsync();
        await using var _ = server;
        var library = await SeedFileWithPosterAsync(server);
        await TestDatabase.ExecuteAsync(server, "INSERT INTO files (library_id, relative_path, status, last_seen_at) VALUES ($l, 'Other/other.mkv', 'processed', CURRENT_TIMESTAMP)", ("$l", library));

        using var response = await client.GetAsync("/api/v1/processing/files");

        var files = (await Json(response))["files"]!.AsArray().ToDictionary(file => file!["relative_path"]!.GetValue<string>());
        Assert.Equal($"/api/v1/artwork/posters/{PosterId}", files[FilePath]!["poster_url"]!.GetValue<string>());
        Assert.Null(files["Other/other.mkv"]!["poster_url"]);
    }

    [Fact]
    public async Task An_activity_entry_about_a_file_carries_that_files_poster()
    {
        var (server, client) = await StartSignedInAsync();
        await using var _ = server;
        var library = await SeedFileWithPosterAsync(server);
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO activity_events (event_type, module, title, library_id, relative_path) VALUES ('processing.remux_pass.completed', 'processing', 'Finished', $l, $p)",
            ("$l", library),
            ("$p", FilePath));

        using var response = await client.GetAsync("/api/v1/activity/recent?module=processing");

        var item = Assert.Single((await Json(response))["items"]!.AsArray())!;
        Assert.Equal($"/api/v1/artwork/posters/{PosterId}", item["poster_url"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_library_file_carries_its_poster()
    {
        var (server, client) = await StartSignedInAsync();
        await using var _ = server;
        var library = await SeedFileWithPosterAsync(server);
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO library_files (library_id, path, size_bytes, mtime, classification) VALUES ($l, $p, 1, 1, 'matches')",
            ("$l", library),
            ("$p", FilePath));

        using var response = await client.GetAsync($"/api/v1/processing/libraries/{library}/library-files");

        var file = Assert.Single((await Json(response))["files"]!.AsArray())!;
        Assert.Equal($"/api/v1/artwork/posters/{PosterId}", file["poster_url"]!.GetValue<string>());
    }

    [Fact]
    public async Task Switching_artwork_off_makes_every_poster_address_null_and_on_brings_them_back()
    {
        var (server, client) = await StartSignedInAsync();
        await using var _ = server;
        await SeedFileWithPosterAsync(server);
        var csrf = await client.CsrfAsync();

        using (var off = await client.PutAsync("/api/v1/processing/metadata-provider", new { csrf_token = csrf, provider = string.Empty, artwork_enabled = false }))
        {
            Assert.Equal(HttpStatusCode.OK, off.StatusCode);
            Assert.False((await Json(off))["artwork_enabled"]!.GetValue<bool>());
        }

        using (var hidden = await client.GetAsync("/api/v1/processing/files"))
        {
            Assert.Null((await Json(hidden))["files"]![0]!["poster_url"]);
        }

        using (var on = await client.PutAsync("/api/v1/processing/metadata-provider", new { csrf_token = csrf, provider = string.Empty, artwork_enabled = true }))
        {
            Assert.True((await Json(on))["artwork_enabled"]!.GetValue<bool>());
        }

        using var shown = await client.GetAsync("/api/v1/processing/files");
        Assert.NotNull((await Json(shown))["files"]![0]!["poster_url"]);
    }

    [Fact]
    public async Task Artwork_is_on_by_default_and_a_save_that_leaves_it_out_keeps_it_as_it_is()
    {
        var (server, client) = await StartSignedInAsync();
        await using var _ = server;
        var csrf = await client.CsrfAsync();

        using (var initial = await client.GetAsync("/api/v1/processing/metadata-provider"))
        {
            Assert.True((await Json(initial))["artwork_enabled"]!.GetValue<bool>());
        }

        using (await client.PutAsync("/api/v1/processing/metadata-provider", new { csrf_token = csrf, artwork_enabled = false }))
        {
        }

        using var saved = await client.PutAsync("/api/v1/processing/metadata-provider", new { csrf_token = csrf, provider = string.Empty });
        Assert.False((await Json(saved))["artwork_enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task The_provider_test_accepts_the_artwork_field_the_page_sends_with_it()
    {
        var (server, client) = await StartSignedInAsync();
        await using var _ = server;

        using var response = await client.PostAsync("/api/v1/processing/metadata-provider/test", new { csrf_token = await client.CsrfAsync(), provider = string.Empty, artwork_enabled = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
