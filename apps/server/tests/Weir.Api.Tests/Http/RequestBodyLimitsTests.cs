using System.Net;
using System.Text;
using Weir.Api.Http;
using Weir.Api.Tests.Platform;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.Http;

/// <summary>
/// A request body is read before any route checks who sent it, so its limits protect every route, including the
/// unauthenticated media-manager webhook.
/// </summary>
public sealed class RequestBodyLimitsTests
{
    [Fact]
    public async Task Deeply_nested_json_on_the_open_webhook_is_refused_and_the_server_keeps_answering()
    {
        await using var server = await WeirTestServer.StartAsync([("WEIR_SESSION_SECRET", Secret), ("WEIR_PROCESSING_WORKER_COUNT", "0")]);
        var client = new ApiTestClient(server);

        using var nested = await client.SendAsync(
            HttpMethod.Post, "/api/v1/intake/webhook/sonarr", content: TestDatabase.RawJson(new string('[', 20_000) + new string(']', 20_000)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, nested.StatusCode);
        Assert.Equal("json_invalid", (await Json(nested))["detail"]![0]!["type"]!.GetValue<string>());
        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task A_body_larger_than_the_limit_is_refused_with_413()
    {
        await using var server = await WeirTestServer.StartAsync([("WEIR_SESSION_SECRET", Secret), ("WEIR_PROCESSING_WORKER_COUNT", "0")]);
        var padding = new string(' ', PyRequestBody.MaxBodyBytes);

        using var response = await new ApiTestClient(server).SendAsync(
            HttpMethod.Post, "/api/v1/intake/webhook/sonarr", content: new StringContent("{}" + padding, Encoding.UTF8, "application/json"));

        Assert.Equal(
            (HttpStatusCode.RequestEntityTooLarge, "The request body is larger than Weir accepts."),
            (response.StatusCode, await Detail(response)));
    }
}
