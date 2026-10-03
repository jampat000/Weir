using System.Net;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>Who may read <c>/metrics</c>: operator and admin sessions, or a configured bearer token.</summary>
[ContractArea("system")]
public sealed class MetricsAuthTests(MetricsAuthTests.BearerTokenServerFixture fixture) : IClassFixture<MetricsAuthTests.BearerTokenServerFixture>
{
    private const string MetricsToken = "metrics-secret-token";

    /// <summary>One server for the class: a configured bearer token does not change how sessions are treated.</summary>
    public sealed class BearerTokenServerFixture : UsersFixture
    {
        protected override IReadOnlyDictionary<string, string> Environment =>
            new Dictionary<string, string> { ["WEIR_METRICS_BEARER_TOKEN"] = MetricsToken };
    }

    [Fact]
    public async Task Metrics_requires_authentication()
    {
        using var client = fixture.Server.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/metrics")).Status);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).Status);
    }

    [Fact]
    public async Task Metrics_allows_operator_or_admin_session()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var response = await admin.GetAsync("/metrics");
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        Assert.Contains("weir_http_requests_total", response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Metrics_forbids_viewer_session()
    {
        using var viewer = await SeededAccounts.SignInViewerAsync(fixture.Server);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/metrics")).Status);
    }

    [Fact]
    public async Task Metrics_allows_bearer_token_when_configured()
    {
        using var client = fixture.Server.CreateClient();
        var response = await client.GetAsync("/metrics", new Dictionary<string, string> { ["Authorization"] = $"Bearer {MetricsToken}" });
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        Assert.Contains("weir_http_requests_total", response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Metrics_rejects_invalid_bearer_token()
    {
        using var client = fixture.Server.CreateClient();
        var response = await client.GetAsync("/metrics", new Dictionary<string, string> { ["Authorization"] = "Bearer wrong-token" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
    }
}
