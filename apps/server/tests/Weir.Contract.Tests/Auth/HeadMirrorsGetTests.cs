using System.Net;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Auth;

/// <summary>HEAD answers like GET, without a body.</summary>
[ContractArea("auth")]
public sealed class HeadMirrorsGetTests(ServerFixture fixture) : AuthTestBase(fixture), IClassFixture<ServerFixture>
{
    [Fact]
    public async Task Head_on_a_get_route_answers_like_get()
    {
        var session = NewSession();
        var get = await session.GetAsync("/health");
        var head = await session.HeadAsync("/health");

        Assert.Equal(HttpStatusCode.OK, get.Status);
        Assert.Equal(HttpStatusCode.OK, head.Status);
        Assert.Equal(string.Empty, head.Text);
    }

    [Fact]
    public async Task Head_carries_the_same_content_type_as_get()
    {
        var session = NewSession();
        var get = await session.GetAsync("/health");
        var head = await session.HeadAsync("/health");

        Assert.Equal(get.Header("content-type"), head.Header("content-type"));
    }

    [Fact]
    public async Task Head_on_the_readiness_probe_answers()
    {
        var head = await NewSession().HeadAsync("/ready");

        Assert.True(head.Status is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable, head.ToString());
        Assert.Equal(string.Empty, head.Text);
    }

    /// <summary>
    /// A POST-only webhook has nothing for HEAD to describe: 405, not 404. This runs without a bundled web app; with
    /// WEIR_WEB_DIST set, the web app's catch-all mount answers GET and HEAD on this path with 404 instead.
    /// </summary>
    [Fact]
    public async Task Head_on_a_post_only_route_says_method_not_allowed()
    {
        await using var server = await WeirServer.StartNewAsync(new Dictionary<string, string> { ["WEIR_WEB_DIST"] = string.Empty });

        var head = await NewSession(server).HeadAsync("/api/v1/intake/webhook/deluno");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, head.Status);
    }

    [Fact]
    public async Task Head_on_a_path_that_does_not_exist_is_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await NewSession().HeadAsync("/api/v1/nothing-here")).Status);
    }

    /// <summary>The middleware must not strip bodies from ordinary requests.</summary>
    [Fact]
    public async Task Get_still_returns_a_body()
    {
        var response = await NewSession().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("ok", (string)response.Fields["status"]!);
    }
}
