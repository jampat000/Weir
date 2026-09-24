using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.Platform;

/// <summary>
/// First-run bootstrap from a device other than the one Weir runs on needs the setup code Weir writes to its
/// own log and data folder at start-up (fix for the DNS-rebinding/LAN-race admin takeover).
/// </summary>
public sealed class SetupCodeApiTests
{
    private static string SetupCodePath(WeirTestServer server) => Path.Join(server.Home, "setup-code");

    private static async Task<HttpContext> BootstrapFromPeerAsync(WeirTestServer server, string peer, string username, string? setupCode)
    {
        var csrf = await new ApiTestClient(server).CsrfAsync();
        var payload = new Dictionary<string, object?>
        {
            ["username"] = username,
            ["password"] = "some-long-password-here",
            ["csrf_token"] = csrf,
        };
        if (setupCode is not null)
        {
            payload["setup_code"] = setupCode;
        }

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        return await server.TestServer.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            context.Request.Method = "POST";
            context.Request.Path = "/api/v1/auth/bootstrap";
            context.Request.Headers.ContentType = "application/json";
            context.Request.Body = new MemoryStream(body);
        });
    }

    private static async Task<string> BodyAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task Startup_writes_a_setup_code_file_while_no_admin_exists()
    {
        await using var server = await StartServerAsync();

        Assert.True(File.Exists(SetupCodePath(server)));
        var code = (await File.ReadAllTextAsync(SetupCodePath(server))).Trim();
        Assert.Matches("^[A-Z0-9]{4}-[A-Z0-9]{4}$", code);
    }

    [Fact]
    public async Task A_loopback_bootstrap_needs_no_setup_code()
    {
        await using var server = await StartServerAsync();

        var context = await BootstrapFromPeerAsync(server, "127.0.0.1", "owner1", setupCode: null);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.False(File.Exists(SetupCodePath(server)));
    }

    [Fact]
    public async Task A_non_loopback_bootstrap_with_no_setup_code_is_refused()
    {
        await using var server = await StartServerAsync();

        var context = await BootstrapFromPeerAsync(server, "203.0.113.4", "owner1", setupCode: null);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Contains("setup code", await BodyAsync(context), StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(SetupCodePath(server)));
    }

    [Fact]
    public async Task A_non_loopback_bootstrap_with_the_wrong_setup_code_is_refused()
    {
        await using var server = await StartServerAsync();

        var context = await BootstrapFromPeerAsync(server, "203.0.113.4", "owner1", setupCode: "WRNG-CODE");

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.True(File.Exists(SetupCodePath(server)));
    }

    [Fact]
    public async Task A_non_loopback_bootstrap_with_the_right_setup_code_succeeds_and_removes_the_file()
    {
        await using var server = await StartServerAsync();
        var code = (await File.ReadAllTextAsync(SetupCodePath(server))).Trim();

        var context = await BootstrapFromPeerAsync(server, "203.0.113.4", "owner1", code);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.False(File.Exists(SetupCodePath(server)));
    }

    [Fact]
    public async Task Bootstrap_status_reports_whether_this_peer_needs_a_setup_code()
    {
        await using var server = await StartServerAsync();

        var loopback = await server.TestServer.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
            context.Request.Path = "/api/v1/auth/bootstrap/status";
        });
        var remote = await server.TestServer.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.4");
            context.Request.Path = "/api/v1/auth/bootstrap/status";
        });

        Assert.Contains("\"requires_setup_code\":false", await BodyAsync(loopback));
        Assert.Contains("\"requires_setup_code\":true", await BodyAsync(remote));
    }
}
