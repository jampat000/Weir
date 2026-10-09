using System.Net;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.Platform;

/// <summary>
/// The buttons in System › About ask the tray for a step by writing a flag file in the data folder: check now, download now,
/// or restart and apply. The state the page reads says the step is under way from the moment it is asked for, and the tray's
/// own answer, written to update-state.json, takes over from there.
/// </summary>
public sealed class SuiteUpdateRequestsApiTests
{
    private const string CheckUpdate = "/api/v1/suite/check-update";
    private const string DownloadUpdate = "/api/v1/suite/download-update";
    private const string UpdateState = "/api/v1/suite/update-state";

    private static async Task<(WeirTestServer Server, ApiTestClient Client)> SignedInAdminAsync()
    {
        var server = await StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        return (server, client);
    }

    private static void TrayWrites(WeirTestServer server, string json) =>
        File.WriteAllText(Path.Join(server.Home, "update-state.json"), json);

    private static async Task<HttpResponseMessage> AskAsync(ApiTestClient client, string path) =>
        await client.PostAsync(path, new { csrf_token = await client.CsrfAsync() });

    private static async Task<(string State, bool Downloaded, string? Version, string? Failure)> StateAsync(ApiTestClient client)
    {
        using var response = await client.GetAsync(UpdateState);
        var body = await Json(response);
        return (body["state"]!.GetValue<string>(), body["downloaded"]!.GetValue<bool>(), body["pending_version"]?.GetValue<string>(), body["failure"]?.GetValue<string>());
    }

    [Fact]
    public async Task Nothing_under_way_reads_as_idle()
    {
        var (server, client) = await SignedInAdminAsync();
        await using var _ = server;

        Assert.Equal(("idle", false, null, null), await StateAsync(client));
    }

    [Theory]
    [InlineData(CheckUpdate, "update-check-now", "checking")]
    [InlineData(DownloadUpdate, "update-download-now", "downloading")]
    public async Task Asking_writes_the_flag_for_the_tray_and_the_state_says_the_step_is_under_way(string path, string flag, string step)
    {
        var (server, client) = await SignedInAdminAsync();
        await using var _ = server;

        using var asked = await AskAsync(client, path);

        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        Assert.Equal(step, (await Json(asked))["state"]!.GetValue<string>());
        Assert.True(File.Exists(Path.Join(server.Home, flag)));
        Assert.Equal((step, false, null, null), await StateAsync(client));
    }

    [Theory]
    [InlineData(CheckUpdate)]
    [InlineData(DownloadUpdate)]
    public async Task Asking_again_while_the_step_is_under_way_is_answered_without_a_second_flag(string path)
    {
        var (server, client) = await SignedInAdminAsync();
        await using var _ = server;
        using var first = await AskAsync(client, path);
        var flag = Directory.GetFiles(server.Home, "update-*-now").Single();
        File.Delete(flag);
        TrayWrites(server, path == CheckUpdate ? """{"state": "checking", "downloaded": false}""" : """{"state": "downloading", "downloaded": false, "version": "9.9.9"}""");

        using var again = await AskAsync(client, path);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Empty(Directory.GetFiles(server.Home, "update-*-now"));
    }

    [Fact]
    public async Task A_download_cannot_be_asked_for_while_a_check_runs_and_a_check_not_while_a_download_runs()
    {
        var (server, client) = await SignedInAdminAsync();
        await using var _ = server;

        TrayWrites(server, """{"state": "checking", "downloaded": false}""");
        using var downloadDuringCheck = await AskAsync(client, DownloadUpdate);
        TrayWrites(server, """{"state": "downloading", "downloaded": false, "version": "9.9.9"}""");
        using var checkDuringDownload = await AskAsync(client, CheckUpdate);

        Assert.Equal(HttpStatusCode.Conflict, downloadDuringCheck.StatusCode);
        Assert.Equal("Weir is still checking for updates.", (await Json(downloadDuringCheck))["detail"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Conflict, checkDuringDownload.StatusCode);
        Assert.Equal("Weir is downloading an update right now.", (await Json(checkDuringDownload))["detail"]!.GetValue<string>());
        Assert.Empty(Directory.GetFiles(server.Home, "update-*-now"));
    }

    [Theory]
    [InlineData(CheckUpdate)]
    [InlineData(DownloadUpdate)]
    public async Task Nothing_is_asked_for_once_the_update_is_downloaded(string path)
    {
        var (server, client) = await SignedInAdminAsync();
        await using var _ = server;
        TrayWrites(server, """{"state": "downloaded", "downloaded": true, "version": "9.9.9"}""");

        using var asked = await AskAsync(client, path);

        Assert.Equal(HttpStatusCode.Conflict, asked.StatusCode);
        Assert.Equal("An update is already downloaded. Restart Weir to apply it.", (await Json(asked))["detail"]!.GetValue<string>());
        Assert.Empty(Directory.GetFiles(server.Home, "update-*-now"));
    }

    [Fact]
    public async Task A_failed_step_reads_with_its_reason_until_the_next_one_is_asked_for()
    {
        var (server, client) = await SignedInAdminAsync();
        await using var _ = server;
        TrayWrites(server, """{"state": "failed", "downloaded": false, "version": "9.9.9", "failure": "Weir could not download the update."}""");

        Assert.Equal(("failed", false, "9.9.9", "Weir could not download the update."), await StateAsync(client));

        using var asked = await AskAsync(client, DownloadUpdate);

        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        Assert.Equal(("downloading", false, "9.9.9", null), await StateAsync(client));
    }

    [Fact]
    public async Task A_state_the_tray_did_not_write_properly_reads_as_idle()
    {
        var (server, client) = await SignedInAdminAsync();
        await using var _ = server;

        TrayWrites(server, """{"state": "exploding", "downloaded": false, "failure": "ignored"}""");
        Assert.Equal(("idle", false, null, null), await StateAsync(client));

        TrayWrites(server, """{"state": "checking", "downloaded": true, "version": "9.9.9"}""");
        Assert.Equal(("downloaded", true, "9.9.9", null), await StateAsync(client));
    }

    [Theory]
    [InlineData(CheckUpdate)]
    [InlineData(DownloadUpdate)]
    public async Task Only_a_signed_in_administrator_with_a_token_can_ask(string path)
    {
        var (server, admin) = await SignedInAdminAsync();
        await using var _ = server;
        await TestDatabase.SeedUserAsync(server, "opal", "operator-password-here", "operator");
        var operatorClient = new ApiTestClient(server);
        await operatorClient.SignInAsync("opal", "operator-password-here");

        using var anonymous = await new ApiTestClient(server).PostAsync(path, new { csrf_token = "x" });
        using var notAdmin = await AskAsync(operatorClient, path);
        using var noToken = await admin.PostAsync(path, new { });
        using var wrongToken = await admin.PostAsync(path, new { csrf_token = "not-a-token" });
        using var extra = await admin.PostAsync(path, new { csrf_token = await admin.CsrfAsync(), step = "now" });

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, notAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noToken.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongToken.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, extra.StatusCode);
        Assert.Empty(Directory.GetFiles(server.Home, "update-*-now"));
    }
}
