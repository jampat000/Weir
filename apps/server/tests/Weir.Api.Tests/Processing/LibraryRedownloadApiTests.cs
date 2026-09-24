using System.Net;
using Weir.Api.Tests.Platform;
using Weir.Core.MediaManagers;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.Processing;

/// <summary>
/// "Download again" (#509) over real HTTP, as the Library file panel uses it: which files a past clean left
/// missing a track the library's rules now keep, and the refusal in front of the destructive request.
/// </summary>
public sealed class LibraryRedownloadApiTests
{
    private const string FilmPath = "/library/films/Arrival (2016)/Arrival.mkv";

    private static string RedownloadsPath(long libraryId) => $"/api/v1/processing/libraries/{libraryId}/library-redownloads";

    private static async Task<(WeirTestServer Server, ApiTestClient Client, long LibraryId)> StartAsync()
    {
        var server = await StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var libraryId = await TestDatabase.ScalarAsync(
            server,
            "INSERT INTO libraries (name, media_type, watched_folder, output_folder, work_folder, display_order) " +
            "VALUES ('Films', 'movie', '/in', '/out', '/work', 9) RETURNING id");
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO library_files (library_id, path, size_bytes, mtime, classification, removed_audio_tracks, " +
            "removed_subtitle_tracks, estimated_bytes_saved, probe_json) VALUES ($library, $path, 1000, 1700000000, " +
            "'matches', 0, 0, 0, '{\"streams\": []}')",
            ("$library", libraryId),
            ("$path", FilmPath));
        return (server, client, libraryId);
    }

    private static Task RecordRemovedTrackAsync(WeirTestServer server, long libraryId, string language) =>
        TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO removed_tracks (library_id, relative_path, language, track_type, codec, reason) " +
            "VALUES ($library, $path, $language, 'audio', 'eac3', 'Not a language your rules keep')",
            ("$library", libraryId),
            ("$path", FilmPath),
            ("$language", language));

    [Fact]
    public async Task A_file_missing_a_track_the_rules_now_keep_is_listed_with_why_it_cannot_be_fetched_automatically()
    {
        var (server, client, libraryId) = await StartAsync();
        await using var _ = server;
        await RecordRemovedTrackAsync(server, libraryId, "eng");

        using var response = await client.GetAsync(RedownloadsPath(libraryId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var title = (await Json(response))["titles"]!.AsArray().Single()!;
        Assert.Equal(FilmPath, title["path"]!.GetValue<string>());
        Assert.False(title["can_redownload"]!.GetValue<bool>());
        Assert.Equal(ManagerRedownloadRules.NoManagerMessage, title["unavailable_reason"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_removed_track_the_rules_still_remove_is_not_listed()
    {
        var (server, client, libraryId) = await StartAsync();
        await using var _ = server;
        await RecordRemovedTrackAsync(server, libraryId, "ita");

        using var response = await client.GetAsync(RedownloadsPath(libraryId));

        Assert.Equal(0, (await Json(response))["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task Asking_for_a_download_without_confirming_it_is_refused()
    {
        var (server, client, libraryId) = await StartAsync();
        await using var _ = server;

        using var response = await client.PostAsync(
            RedownloadsPath(libraryId),
            new { path = FilmPath, confirm_destructive = false, csrf_token = await client.CsrfAsync() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
