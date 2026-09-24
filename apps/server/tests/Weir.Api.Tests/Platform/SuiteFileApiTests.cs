using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Weir.Infrastructure.Logging;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.Platform;

/// <summary>"Back up now" and the server log download, over real HTTP.</summary>
public sealed class SuiteFileApiTests
{
    private static async Task<(WeirTestServer Server, ApiTestClient Admin)> SignedInAdminAsync()
    {
        var server = await StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        return (server, client);
    }

    private static async Task<ApiTestClient> SignedInAsync(WeirTestServer server, string username, string role)
    {
        await TestDatabase.SeedUserAsync(server, username, "a-long-enough-password", role);
        var client = new ApiTestClient(server);
        await client.SignInAsync(username, "a-long-enough-password");
        return client;
    }

    [Fact]
    public async Task Backing_up_now_writes_a_snapshot_that_is_then_listed()
    {
        var (server, admin) = await SignedInAdminAsync();
        await using var _ = server;

        using var created = await admin.PostAsync("/api/v1/suite/configuration-backups", new { csrf_token = await admin.CsrfAsync() });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created))["id"]!.GetValue<long>();
        var listed = (await Json(await admin.GetAsync("/api/v1/suite/configuration-backups")))["items"]!.AsArray();
        Assert.Equal(id, listed.Single()!["id"]!.GetValue<long>());
    }

    [Fact]
    public async Task Backing_up_now_is_refused_to_an_operator()
    {
        var (server, _) = await SignedInAdminAsync();
        await using var __ = server;
        var operatorClient = await SignedInAsync(server, "opal", "operator");

        using var refused = await operatorClient.PostAsync("/api/v1/suite/configuration-backups", new { csrf_token = await operatorClient.CsrfAsync() });

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task The_server_log_downloads_as_a_file_holding_every_line()
    {
        var (server, admin) = await SignedInAdminAsync();
        await using var _ = server;
        var logFile = server.Services.GetRequiredService<WeirLogFile>();
        logFile.WriteLine("{\"timestamp\":\"2026-05-09T10:00:00Z\",\"level\":\"INFO\",\"logger\":\"weir.tests\",\"message\":\"first line\"}");
        logFile.WriteLine("{\"timestamp\":\"2026-05-09T10:00:01Z\",\"level\":\"ERROR\",\"logger\":\"weir.tests\",\"message\":\"second line\"}");

        using var download = await admin.GetAsync("/api/v1/suite/logs/download");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Matches("^attachment; filename=\"weir-log-\\d{8}-\\d{6}\\.log\"$", Header(download, "Content-Disposition"));
        var text = await download.Content.ReadAsStringAsync();
        Assert.Contains("first line", text, StringComparison.Ordinal);
        Assert.Contains("second line", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_viewer_cannot_download_the_server_log()
    {
        var (server, _) = await SignedInAdminAsync();
        await using var __ = server;
        var viewer = await SignedInAsync(server, "vera", "viewer");

        using var refused = await viewer.GetAsync("/api/v1/suite/logs/download");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }
}
