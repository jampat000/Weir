using System.Net;
using System.Text.Json.Nodes;

namespace Weir.Contract.Tests.Harness;

/// <summary>Checks the harness's default and per-request headers against a real server, as a browser-like client sends them.</summary>
[ContractArea("harness")]
public sealed class WeirClientHeadersTests(ServerFixture fixture) : IClassFixture<ServerFixture>
{
    private const string LoginPath = $"{WeirClient.Api}/auth/login";
    private const string MissingHeaderDetail = "X-Requested-With";

    private static readonly IReadOnlyDictionary<string, string> RequestedWith =
        new Dictionary<string, string> { ["X-Requested-With"] = "XMLHttpRequest" };

    private IReadOnlyDictionary<string, string> BrowserOrigin => new Dictionary<string, string>
    {
        ["Origin"] = fixture.Server.BaseUrl.GetLeftPart(UriPartial.Authority),
    };

    [Fact]
    public async Task Default_headers_are_sent_on_every_request()
    {
        using var client = fixture.Server.CreateClient(BrowserOrigin);

        var response = await client.PostAsync(LoginPath, new JsonObject());

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Contains(MissingHeaderDetail, response.Text);
    }

    [Fact]
    public async Task A_client_without_default_headers_sends_no_origin()
    {
        using var client = fixture.Server.CreateClient();

        var response = await client.PostAsync(LoginPath, new JsonObject());

        Assert.DoesNotContain(MissingHeaderDetail, response.Text);
    }

    [Fact]
    public async Task A_header_given_for_one_request_adds_to_the_default_headers()
    {
        using var client = fixture.Server.CreateClient(BrowserOrigin);

        var response = await client.RequestAsync(HttpMethod.Post, LoginPath, new JsonObject(), RequestedWith);

        Assert.DoesNotContain(MissingHeaderDetail, response.Text);
    }

    [Fact]
    public async Task A_browser_like_client_can_create_the_admin_and_sign_in()
    {
        var browserHeaders = new Dictionary<string, string>(BrowserOrigin.Concat(RequestedWith));

        using var client = await fixture.Server.CreateAdminClientAsync(browserHeaders);

        var session = await client.GetAsync($"{WeirClient.Api}/auth/me");
        Assert.Equal(HttpStatusCode.OK, session.Status);
    }
}
