using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>
/// The buttons in System › About ask the tray to check for an update, to download it, or to restart and apply it
/// (<c>/api/v1/suite/check-update</c>, <c>download-update</c> and <c>apply-update</c>). A bare install has no tray, so a
/// request only leaves its flag in the data folder; what these tests judge is who may ask, what is answered, and what
/// <c>/suite/update-state</c> then says. Only the Windows install has a tray, and only while the tray says it is alive
/// (<c>tray-heartbeat.json</c>): without one every request is refused with the reason.
/// </summary>
[ContractArea("system")]
public sealed class UpdateRequestsApiTests(UsersFixture fixture) : IClassFixture<UsersFixture>
{
    private const string CheckUpdate = $"{WeirClient.Api}/suite/check-update";
    private const string DownloadUpdate = $"{WeirClient.Api}/suite/download-update";
    private const string ApplyUpdate = $"{WeirClient.Api}/suite/apply-update";
    private const string UpdateState = $"{WeirClient.Api}/suite/update-state";
    private const string NoTray = "The Weir tray isn't running, so Weir can't update itself from here.";
    private static readonly string[] StateKeys = ["downloaded", "pending_version", "state", "failure", "tray_running"];

    /// <summary>A Windows install whose tray has said, <paramref name="ago"/> ago, that it is alive.</summary>
    private static async Task<WeirServer> StartWindowsInstallAsync(TimeSpan ago)
    {
        var server = await WeirServer.StartNewAsync(new Dictionary<string, string> { ["WEIR_RUNTIME"] = "windows" });
        await File.WriteAllTextAsync(Path.Combine(server.Home, "tray-heartbeat.json"), $$"""{"at": "{{DateTimeOffset.UtcNow - ago:O}}"}""");
        return server;
    }

    [Theory]
    [InlineData(CheckUpdate)]
    [InlineData(DownloadUpdate)]
    public async Task A_request_needs_a_signed_in_administrator(string path)
    {
        using var anonymous = fixture.Server.CreateClient();
        using var viewer = await SeededAccounts.SignInViewerAsync(fixture.Server);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(path, new JsonObject { ["csrf_token"] = "x" })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostWithCsrfAsync(path, new JsonObject())).Status);
    }

    [Theory]
    [InlineData(CheckUpdate)]
    [InlineData(DownloadUpdate)]
    public async Task A_request_without_a_token_of_their_own_is_refused(string path)
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsync(path, new JsonObject())).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(path, new JsonObject { ["csrf_token"] = "not-a-token" })).Status);
    }

    [Theory]
    [InlineData(CheckUpdate, "checking")]
    [InlineData(DownloadUpdate, "downloading")]
    public async Task A_request_is_answered_with_the_step_under_way_and_the_update_state_says_the_same(string path, string step)
    {
        await using var server = await StartWindowsInstallAsync(TimeSpan.Zero);
        using var admin = await server.CreateAdminClientAsync();

        var asked = await admin.PostWithCsrfAsync(path, new JsonObject());
        var again = await admin.PostWithCsrfAsync(path, new JsonObject());
        var state = await admin.GetAsync(UpdateState);

        Assert.Equal(HttpStatusCode.OK, asked.Status);
        Assert.Equal(HttpStatusCode.OK, again.Status);
        foreach (var body in new[] { asked.Fields, again.Fields, state.Fields })
        {
            Assert.Equal(StateKeys, body.Select(field => field.Key));
            Assert.Equal(step, (string?)body["state"]);
            Assert.False((bool)body["downloaded"]!);
        }
    }

    [Fact]
    public async Task A_download_is_refused_while_a_check_is_asked_for()
    {
        await using var server = await StartWindowsInstallAsync(TimeSpan.Zero);
        using var admin = await server.CreateAdminClientAsync();
        await admin.PostWithCsrfAsync(CheckUpdate, new JsonObject());

        var download = await admin.PostWithCsrfAsync(DownloadUpdate, new JsonObject());

        Assert.Equal(HttpStatusCode.Conflict, download.Status);
        Assert.Equal("Weir is still checking for updates.", (string?)download.Fields["detail"]);
    }

    [Theory]
    [InlineData(CheckUpdate)]
    [InlineData(DownloadUpdate)]
    public async Task Once_the_tray_has_downloaded_the_update_nothing_more_is_asked_for(string path)
    {
        await using var server = await StartWindowsInstallAsync(TimeSpan.Zero);
        using var admin = await server.CreateAdminClientAsync();
        await File.WriteAllTextAsync(Path.Combine(server.Home, "update-state.json"), """{"state": "downloaded", "downloaded": true, "version": "9.9.9"}""");

        var asked = await admin.PostWithCsrfAsync(path, new JsonObject());
        var state = await admin.GetAsync(UpdateState);

        Assert.Equal(HttpStatusCode.Conflict, asked.Status);
        Assert.Equal("An update is already downloaded. Restart Weir to apply it.", (string?)asked.Fields["detail"]);
        Assert.Equal(("downloaded", true, "9.9.9"), ((string?)state.Fields["state"], (bool)state.Fields["downloaded"]!, (string?)state.Fields["pending_version"]));
    }

    [Fact]
    public async Task A_failure_the_tray_reports_reads_back_with_its_reason()
    {
        await using var server = await StartWindowsInstallAsync(TimeSpan.Zero);
        using var admin = await server.CreateAdminClientAsync();
        await File.WriteAllTextAsync(
            Path.Combine(server.Home, "update-state.json"),
            """{"state": "failed", "downloaded": false, "version": null, "failure": "Weir could not reach GitHub to look for an update. Check the internet connection and try again."}""");

        var state = (await admin.GetAsync(UpdateState)).Fields;

        Assert.Equal("failed", (string?)state["state"]);
        Assert.Equal("Weir could not reach GitHub to look for an update. Check the internet connection and try again.", (string?)state["failure"]);
        Assert.Null(state["pending_version"]);
    }

    [Fact]
    public async Task With_no_state_from_the_tray_nothing_is_under_way()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var state = (await admin.GetAsync(UpdateState)).Fields;

        Assert.Equal(StateKeys, state.Select(field => field.Key));
        Assert.False((bool)state["tray_running"]!);
        Assert.Equal(("idle", false), (state["state"]!.ToString(), (bool)state["downloaded"]!));
        Assert.Null(state["failure"]);
    }

    [Theory]
    [InlineData(CheckUpdate)]
    [InlineData(DownloadUpdate)]
    [InlineData(ApplyUpdate)]
    public async Task A_bare_install_has_no_tray_so_a_request_is_refused_with_the_reason(string path)
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var asked = await admin.PostWithCsrfAsync(path, new JsonObject());

        Assert.Equal(HttpStatusCode.Conflict, asked.Status);
        Assert.Equal(NoTray, (string?)asked.Fields["detail"]);
        Assert.Empty(Directory.GetFiles(fixture.Server.Home, "update-*-now"));
    }

    [Theory]
    [InlineData(CheckUpdate)]
    [InlineData(DownloadUpdate)]
    [InlineData(ApplyUpdate)]
    public async Task A_tray_that_has_gone_quiet_is_refused_with_the_reason_and_the_state_says_so(string path)
    {
        await using var server = await StartWindowsInstallAsync(TimeSpan.FromMinutes(10));
        using var admin = await server.CreateAdminClientAsync();
        await File.WriteAllTextAsync(Path.Combine(server.Home, "update-state.json"), """{"state": "downloading", "downloaded": false, "version": "9.9.9"}""");

        var asked = await admin.PostWithCsrfAsync(path, new JsonObject());
        var state = (await admin.GetAsync(UpdateState)).Fields;

        Assert.Equal(HttpStatusCode.Conflict, asked.Status);
        Assert.Equal(NoTray, (string?)asked.Fields["detail"]);
        Assert.Equal(("idle", false), ((string?)state["state"], (bool)state["tray_running"]!));
    }
}
