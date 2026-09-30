using System.Net;
using Weir.Api.Tests.Platform;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.Processing;

/// <summary>The rules profile a library cleans its existing files by, chosen on the Library page through library-settings.</summary>
public sealed class LibraryCleanRulesProfileApiTests
{
    private const string ScanJobKind = "processing.library.scan.v1";

    private static async Task<long> AddLibraryAsync(WeirTestServer server, string name, long order) =>
        await TestDatabase.ScalarAsync(
            server,
            "INSERT INTO libraries (name, media_type, watched_folder, output_folder, work_folder, display_order) " +
            $"VALUES ('{name}', 'movie', '/in', '/out', '/work', {order}) RETURNING id");

    private static async Task<long> CreateProfileAsync(ApiTestClient client, string name)
    {
        using var response = await client.PostAsync(
            "/api/v1/processing/rule-sets",
            new { csrf_token = await client.CsrfAsync(), name, primary_audio_lang = "eng" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response))["id"]!.GetValue<long>();
    }

    private static async Task<ApiTestClient> SignedInAdminAsync(WeirTestServer server)
    {
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        return client;
    }

    [Fact]
    public async Task A_library_follows_its_workflows_profile_until_one_is_chosen()
    {
        await using var server = await StartServerAsync();
        var client = await SignedInAdminAsync(server);
        var libraryId = await AddLibraryAsync(server, "Follows", 20);

        using var response = await client.GetAsync($"/api/v1/processing/libraries/{libraryId}/library-settings");

        var body = await Json(response);
        Assert.True(body.AsObject().ContainsKey("library_rule_set_id"));
        Assert.Null(body["library_rule_set_id"]);
    }

    [Fact]
    public async Task Choosing_a_profile_is_kept_by_later_saves_that_leave_it_out_and_cleared_by_null()
    {
        await using var server = await StartServerAsync();
        var client = await SignedInAdminAsync(server);
        var libraryId = await AddLibraryAsync(server, "Chooses", 21);
        var profileId = await CreateProfileAsync(client, "Library cleaning");
        var settingsPath = $"/api/v1/processing/libraries/{libraryId}/library-settings";

        using var chosen = await client.PutAsync(
            settingsPath,
            new { library_folders = new[] { "/library/films" }, library_rule_set_id = profileId, csrf_token = await client.CsrfAsync() });
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
        Assert.Equal(profileId, (await Json(chosen))["library_rule_set_id"]!.GetValue<long>());

        using var leftOut = await client.PutAsync(
            settingsPath,
            new { library_folders = new[] { "/library/films" }, csrf_token = await client.CsrfAsync() });
        Assert.Equal(profileId, (await Json(leftOut))["library_rule_set_id"]!.GetValue<long>());

        using var cleared = await client.PutAsync(
            settingsPath,
            new { library_folders = new[] { "/library/films" }, library_rule_set_id = (long?)null, csrf_token = await client.CsrfAsync() });
        Assert.Null((await Json(cleared))["library_rule_set_id"]);
    }

    [Fact]
    public async Task A_profile_that_does_not_exist_is_refused_and_nothing_is_saved()
    {
        await using var server = await StartServerAsync();
        var client = await SignedInAdminAsync(server);
        var libraryId = await AddLibraryAsync(server, "Unknown profile", 22);

        using var response = await client.PutAsync(
            $"/api/v1/processing/libraries/{libraryId}/library-settings",
            new { library_folders = new[] { "/library/films" }, library_rule_set_id = 987654, csrf_token = await client.CsrfAsync() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("That rules profile no longer exists. Choose another one.", await Detail(response));
        Assert.Equal(0, await TestDatabase.ScalarAsync(server, $"SELECT COUNT(*) FROM library_folders WHERE library_id = {libraryId}"));
    }

    [Fact]
    public async Task Changing_the_profile_checks_the_library_again_in_the_background()
    {
        await using var server = await StartServerAsync();
        var client = await SignedInAdminAsync(server);
        var libraryId = await AddLibraryAsync(server, "Rescans", 23);
        var profileId = await CreateProfileAsync(client, "Rescan profile");
        var settingsPath = $"/api/v1/processing/libraries/{libraryId}/library-settings";
        var scansBefore = await TestDatabase.ScalarAsync(server, $"SELECT COUNT(*) FROM jobs WHERE job_kind = '{ScanJobKind}'");

        using var chosen = await client.PutAsync(
            settingsPath,
            new { library_folders = new[] { "/library/films" }, library_rule_set_id = profileId, csrf_token = await client.CsrfAsync() });
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
        var scansAfterChoosing = await TestDatabase.ScalarAsync(server, $"SELECT COUNT(*) FROM jobs WHERE job_kind = '{ScanJobKind}'");

        using var sameAgain = await client.PutAsync(
            settingsPath,
            new { library_folders = new[] { "/library/films" }, library_rule_set_id = profileId, csrf_token = await client.CsrfAsync() });
        Assert.Equal(HttpStatusCode.OK, sameAgain.StatusCode);
        var scansAfterSaveWithoutChange = await TestDatabase.ScalarAsync(server, $"SELECT COUNT(*) FROM jobs WHERE job_kind = '{ScanJobKind}'");

        Assert.Equal(scansBefore + 1, scansAfterChoosing);
        Assert.Equal(scansAfterChoosing, scansAfterSaveWithoutChange);
    }

    [Fact]
    public async Task A_profile_a_library_cleans_by_cannot_be_deleted()
    {
        await using var server = await StartServerAsync();
        var client = await SignedInAdminAsync(server);
        var libraryId = await AddLibraryAsync(server, "Holds a profile", 24);
        var profileId = await CreateProfileAsync(client, "In use for cleaning");
        using var chosen = await client.PutAsync(
            $"/api/v1/processing/libraries/{libraryId}/library-settings",
            new { library_folders = new[] { "/library/films" }, library_rule_set_id = profileId, csrf_token = await client.CsrfAsync() });
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);

        using var deleted = await client.SendAsync(
            HttpMethod.Delete, $"/api/v1/processing/rule-sets/{profileId}", new { csrf_token = await client.CsrfAsync() });

        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        Assert.Contains("still used by", await Detail(deleted), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_viewer_cannot_choose_a_profile()
    {
        await using var server = await StartServerAsync();
        var client = await SignedInAdminAsync(server);
        var libraryId = await AddLibraryAsync(server, "Viewer", 25);
        var profileId = await CreateProfileAsync(client, "Not for viewers");
        await TestDatabase.SeedViewerAsync(server);
        var viewer = new ApiTestClient(server);
        await viewer.SignInAsync("bob", ViewerPassword);

        using var response = await viewer.PutAsync(
            $"/api/v1/processing/libraries/{libraryId}/library-settings",
            new { library_folders = new[] { "/library/films" }, library_rule_set_id = profileId, csrf_token = await viewer.CsrfAsync() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
