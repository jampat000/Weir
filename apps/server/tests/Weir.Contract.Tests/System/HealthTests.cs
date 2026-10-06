using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>Health, readiness, request ids, and how unknown paths and app routes are answered.</summary>
[ContractArea("system")]
public sealed class HealthTests(HealthTests.PackagedWebServerFixture fixture) : IClassFixture<HealthTests.PackagedWebServerFixture>
{
    /// <summary>A packaged web build of our own, so the SPA fallback tests know exactly what the shell holds.</summary>
    public sealed class PackagedWebServerFixture : IAsyncLifetime
    {
        private readonly string _webDist = Directory.CreateTempSubdirectory("weir_contract_web_").FullName;
        private WeirServer? _server;

        public WeirServer Server => _server ?? throw new InvalidOperationException("The server has not started yet.");

        public async Task InitializeAsync()
        {
            await File.WriteAllTextAsync(Path.Combine(_webDist, "index.html"), "<!doctype html><title>Weir</title>");
            _server = await WeirServer.StartNewAsync(new Dictionary<string, string> { ["WEIR_WEB_DIST"] = _webDist });
        }

        public async Task DisposeAsync()
        {
            if (_server is not null)
            {
                await _server.DisposeAsync();
            }

            Directory.Delete(_webDist, recursive: true);
        }
    }

    [Fact]
    public async Task Health_ok()
    {
        using var client = fixture.Server.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.True(
            JsonNode.DeepEquals(
                JsonNode.Parse("""{"status": "ok", "dependencies": {"database": "ok"}}"""),
                response.Json),
            response.Text);
        Assert.StartsWith("no-store", response.Header("Cache-Control") ?? "", StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(response.Header("X-Request-ID")));
    }

    [Fact]
    public async Task Request_id_header_is_echoed()
    {
        using var client = fixture.Server.CreateClient();
        var response = await client.GetAsync("/health", new Dictionary<string, string> { ["X-Request-ID"] = "audit-request-1" });

        Assert.Equal("audit-request-1", response.Header("X-Request-ID"));
    }

    [Fact]
    public async Task Ready_ok_after_lifespan_startup()
    {
        using var client = fixture.Server.CreateClient();
        var response = await client.GetAsync("/ready");
        Assert.Equal(HttpStatusCode.OK, response.Status);
        var body = response.Fields;
        Assert.Equal(JsonValueKind.True, body["ready"]!.GetValueKind());
        Assert.Equal("ready", (string?)body["status"]);
        Assert.Equal(["ready", "status"], body.Select(field => field.Key).Order(StringComparer.Ordinal));
        Assert.StartsWith("no-store", response.Header("Cache-Control") ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_upgrade_api_browser_landing_redirects_to_system_about()
    {
        using var client = fixture.Server.CreateClient();
        var response = await client.GetAsync($"{WeirClient.Api}/suite/upgrade-now");

        Assert.Equal(HttpStatusCode.SeeOther, response.Status);
        Assert.Equal("/system?tab=about", response.Header("location"));
    }

    [Fact]
    public async Task Regular_unknown_api_path_still_returns_json_404()
    {
        using var client = fixture.Server.CreateClient();
        var response = await client.GetAsync($"{WeirClient.Api}/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"detail": "Not Found"}"""), response.Json), response.Text);
    }

    [Fact]
    public async Task Packaged_app_routes_refresh_to_react_shell()
    {
        using var client = fixture.Server.CreateClient();
        var response = await client.GetAsync("/setup/workflows", new Dictionary<string, string> { ["Accept"] = "text/html" });

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Contains("text/html", response.Header("content-type"), StringComparison.Ordinal);
        Assert.Contains("Weir", response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_non_app_path_still_returns_404()
    {
        using var client = fixture.Server.CreateClient();
        var response = await client.GetAsync("/not-a-real-route");

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
    }
}
