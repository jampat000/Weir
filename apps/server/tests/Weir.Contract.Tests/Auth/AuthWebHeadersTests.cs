using System.Net;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Auth.AuthSupport;

namespace Weir.Contract.Tests.Auth;

/// <summary>Security headers on the API, and how the bundled web app and its static assets are served.</summary>
[ContractArea("auth")]
public sealed class AuthWebHeadersTests(AuthWebHeadersTests.WebServerFixture fixture) : AuthTestBase(fixture), IClassFixture<AuthWebHeadersTests.WebServerFixture>
{
    private const string Api = AuthSession.Api;

    [Fact]
    public async Task Security_headers_on_health_and_api()
    {
        var session = NewSession();
        var health = await RawHeadersAsync(session, "/health");
        Assert.Equal("nosniff", health("X-Content-Type-Options"));
        Assert.Equal("DENY", health("X-Frame-Options"));
        Assert.Equal("strict-origin-when-cross-origin", health("Referrer-Policy"));
        Assert.False(string.IsNullOrEmpty(health("Content-Security-Policy")));
        Assert.StartsWith("no-store", health("Cache-Control") ?? string.Empty, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(health("strict-transport-security")));
        var csrf = await RawHeadersAsync(session, $"{Api}/auth/csrf");
        Assert.False(string.IsNullOrEmpty(csrf("Content-Security-Policy")));
        Assert.Contains("frame-ancestors", (csrf("Content-Security-Policy") ?? string.Empty).ToLowerInvariant(), StringComparison.Ordinal);
        Assert.StartsWith("no-store", csrf("Cache-Control") ?? string.Empty, StringComparison.Ordinal);
        foreach (var path in new[] { $"{Api}/system/directories", $"{Api}/suite/security-overview", $"{Api}/suite/update-status", "/metrics" })
        {
            var headers = await RawHeadersAsync(session, path);
            Assert.True((headers("Cache-Control") ?? string.Empty).StartsWith("no-store", StringComparison.Ordinal), path);
        }
    }

    [Fact]
    public async Task Static_assets_do_not_get_api_no_store()
    {
        var (response, _) = await NewSession().GetRawAsync("/assets/app.js");
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEqual("no-store, private", AuthSession.RawHeader(response, "Cache-Control"));
        }
    }

    [Fact]
    public async Task Bundled_html_csp_does_not_allow_inline_styles()
    {
        var response = await NewSession().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var csp = response.Header("Content-Security-Policy") ?? string.Empty;
        Assert.Contains("style-src 'self'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("fonts.googleapis.com", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("fonts.gstatic.com", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-inline'", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Spa_login_route_serves_index_html()
    {
        var response = await NewSession().GetAsync("/login?session=expired", Headers(("Accept", "text/html")));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Contains("text/html", (response.Header("content-type") ?? string.Empty).ToLowerInvariant(), StringComparison.Ordinal);
        Assert.Contains("Weir", response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_static_asset_still_returns_404()
    {
        var response = await NewSession().GetAsync("/assets/missing.js", Headers(("Accept", "*/*")));

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
    }

    /// <summary>A header lookup over one GET, with the headers exactly as the server wrote them.</summary>
    private static async Task<Func<string, string?>> RawHeadersAsync(AuthSession session, string path)
    {
        var (response, _) = await session.GetRawAsync(path);
        using (response)
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in response.Headers.NonValidated.Concat(response.Content.Headers.NonValidated))
            {
                values[header.Key] = string.Join(", ", header.Value);
            }

            return name => values.GetValueOrDefault(name);
        }
    }

    /// <summary>A server serving a small bundled web app from a folder of its own, with HSTS on.</summary>
    public sealed class WebServerFixture : ServerFixture, IDisposable
    {
        private readonly string _dist = WebDist.Write("<!doctype html><html><body><div id='root'>Weir</div></body></html>", assets: new Dictionary<string, byte[]>
        {
            ["app.js"] = System.Text.Encoding.UTF8.GetBytes("console.log('ok');"),
        });

        protected override IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string>
        {
            ["WEIR_SECURITY_ENABLE_HSTS"] = "1",
            ["WEIR_WEB_DIST"] = _dist,
        };

        public void Dispose() => Directory.Delete(Path.GetDirectoryName(_dist)!, recursive: true);
    }
}
