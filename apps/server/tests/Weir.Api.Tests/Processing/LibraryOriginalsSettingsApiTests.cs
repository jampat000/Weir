using System.Net;
using Weir.Api.Tests.Platform;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.Processing;

/// <summary>#735: the library-settings endpoint's "keep the original after clean" switch and originals folder.</summary>
public sealed class LibraryOriginalsSettingsApiTests
{
    [Fact]
    public async Task The_setting_is_off_with_no_folder_until_someone_turns_it_on()
    {
        var server = await StartServerAsync();
        await using var _ = server;
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var libraryId = await TestDatabase.ScalarAsync(
            server,
            "INSERT INTO libraries (name, media_type, watched_folder, output_folder, work_folder, display_order) " +
            "VALUES ('Originals', 'movie', '/in', '/out', '/work', 9) RETURNING id");
        var settingsPath = $"/api/v1/processing/libraries/{libraryId}/library-settings";

        using var before = await client.GetAsync(settingsPath);
        var beforeBody = await Json(before);
        Assert.False(beforeBody["keep_original_after_clean"]!.GetValue<bool>());
        Assert.Equal(string.Empty, beforeBody["originals_folder"]!.GetValue<string>());

        using var turnedOn = await client.PutAsync(
            settingsPath,
            new
            {
                library_folders = new[] { "/library/films" },
                keep_original_after_clean = true,
                originals_folder = "/library/originals",
                csrf_token = await client.CsrfAsync(),
            });
        Assert.Equal(HttpStatusCode.OK, turnedOn.StatusCode);
        var turnedOnBody = await Json(turnedOn);
        Assert.True(turnedOnBody["keep_original_after_clean"]!.GetValue<bool>());
        Assert.Equal("/library/originals", turnedOnBody["originals_folder"]!.GetValue<string>());

        // Left out of a later save: both stay exactly as they were, like every other optional field here.
        using var untouched = await client.PutAsync(
            settingsPath,
            new { library_folders = new[] { "/library/films" }, csrf_token = await client.CsrfAsync() });
        var untouchedBody = await Json(untouched);
        Assert.True(untouchedBody["keep_original_after_clean"]!.GetValue<bool>());
        Assert.Equal("/library/originals", untouchedBody["originals_folder"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_originals_folder_that_is_not_absolute_is_refused()
    {
        var server = await StartServerAsync();
        await using var _ = server;
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        var libraryId = await TestDatabase.ScalarAsync(
            server,
            "INSERT INTO libraries (name, media_type, watched_folder, output_folder, work_folder, display_order) " +
            "VALUES ('Originals2', 'movie', '/in', '/out', '/work', 10) RETURNING id");

        using var response = await client.PutAsync(
            $"/api/v1/processing/libraries/{libraryId}/library-settings",
            new { library_folders = Array.Empty<string>(), originals_folder = "relative/path", csrf_token = await client.CsrfAsync() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
