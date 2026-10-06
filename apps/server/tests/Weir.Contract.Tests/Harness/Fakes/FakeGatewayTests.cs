using System.Net;
using System.Text.Json.Nodes;

namespace Weir.Contract.Tests.Harness.Fakes;

/// <summary>Checks the fake metadata gateway over real HTTP, so the scenarios that point a server at it can rely on it.</summary>
[ContractArea("harness")]
public sealed class FakeGatewayTests : IDisposable
{
    private readonly FakeGateway _gateway = new();
    private readonly HttpClient _http = new();

    public void Dispose()
    {
        _http.Dispose();
        _gateway.Dispose();
    }

    [Fact]
    public async Task A_taught_title_is_found_in_any_letter_case_with_its_poster_and_original_language()
    {
        _gateway.Knows("nosferatu", "nosferatu.jpg", tmdbId: 653, originalLanguage: "de");

        var found = await Search("query=Nosferatu&mediaType=movie");

        Assert.Equal(1, (int)found["resultCount"]!);
        var result = found["results"]![0]!;
        Assert.Equal("tmdb", (string)result["provider"]!);
        Assert.Equal("653", (string)result["providerId"]!);
        Assert.Equal("Nosferatu", (string)result["title"]!);
        Assert.Equal("de", (string)result["originalLanguage"]!);
        Assert.Equal($"{_gateway.BaseUrl}/artwork/w780/nosferatu.jpg", (string)result["posterUrl"]!);
        var search = Assert.Single(_gateway.Searches());
        Assert.Equal(new Dictionary<string, string[]> { ["query"] = ["Nosferatu"], ["mediaType"] = ["movie"] }, search.Query);
        Assert.Single(_gateway.SearchesFor("NOSFERATU"));
        Assert.Empty(_gateway.SearchesFor("metropolis"));
    }

    [Fact]
    public async Task An_unknown_title_gets_an_empty_result_and_a_provider_id_looks_a_title_up_by_its_id()
    {
        _gateway.Knows("metropolis", "metropolis.jpg", tmdbId: 19);

        var unknown = await Search("query=something+else");
        var byId = await Search("query=a+different+title&providerId=19");
        var unknownId = await Search("query=metropolis&providerId=20");

        Assert.Equal(0, (int)unknown["resultCount"]!);
        Assert.Equal("deluno-broker", (string)unknown["provider"]!);
        Assert.Equal("a different title", (string)byId["results"]![0]!["title"]!);
        Assert.Equal("metropolis.jpg", ((string)byId["results"]![0]!["posterUrl"]!).Split('/')[^1]);
        Assert.Equal(0, (int)unknownId["resultCount"]!);
    }

    [Fact]
    public async Task A_busy_gateway_answers_503_with_retry_after_until_it_is_calm_again()
    {
        _gateway.Knows("metropolis", "metropolis.jpg");
        _gateway.Busy(retryAfterSeconds: 600);

        using var busy = await _http.GetAsync($"{_gateway.BaseUrl}/metadata/search?query=metropolis");
        _gateway.Busy(retryAfterSeconds: null);
        using var busyWithoutHeader = await _http.GetAsync($"{_gateway.BaseUrl}/metadata/search?query=metropolis");
        _gateway.Calm();
        var calm = await Search("query=metropolis");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
        Assert.Equal("600", Assert.Single(busy.Headers.GetValues("Retry-After")));
        Assert.Equal("provider_busy", (string)JsonNode.Parse(await busy.Content.ReadAsStringAsync())!["error"]!);
        Assert.False(busyWithoutHeader.Headers.Contains("Retry-After"));
        Assert.Equal(1, (int)calm["resultCount"]!);
    }

    [Fact]
    public async Task Posters_are_served_at_the_small_artwork_route_only_for_files_the_gateway_was_taught()
    {
        _gateway.Knows("metropolis", "metropolis.jpg");
        _gateway.ServesImage("extra.jpg");

        using var taught = await _http.GetAsync($"{_gateway.BaseUrl}/artwork/w342/metropolis.jpg");
        using var extra = await _http.GetAsync($"{_gateway.BaseUrl}/artwork/w342/extra.jpg");
        using var unknown = await _http.GetAsync($"{_gateway.BaseUrl}/artwork/w342/other.jpg");
        using var wrongSize = await _http.GetAsync($"{_gateway.BaseUrl}/artwork/w780/metropolis.jpg");

        Assert.Equal(HttpStatusCode.OK, taught.StatusCode);
        Assert.Equal("image/jpeg", taught.Content.Headers.ContentType?.MediaType);
        Assert.Equal(FakeGateway.ImageBytes, await taught.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.OK, extra.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrongSize.StatusCode);
        Assert.Equal(4, _gateway.ImageRequests().Count);
    }

    [Fact]
    public async Task The_health_route_answers_and_every_request_is_recorded_in_order()
    {
        using var health = await _http.GetAsync($"{_gateway.BaseUrl}/health");
        using var missing = await _http.GetAsync($"{_gateway.BaseUrl}/elsewhere");

        Assert.Equal("ok", (string)JsonNode.Parse(await health.Content.ReadAsStringAsync())!["status"]!);
        Assert.Equal("deluno-metadata-gateway", (string)JsonNode.Parse(await health.Content.ReadAsStringAsync())!["service"]!);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(["/health", "/elsewhere"], _gateway.Requests.Select(request => request.Path));
    }

    [Fact]
    public void A_server_is_pointed_at_the_gateway_through_its_environment()
    {
        Assert.Equal(_gateway.BaseUrl, _gateway.Env["WEIR_ARTWORK_GATEWAY_URL"]);
    }

    private async Task<JsonNode> Search(string query)
    {
        using var response = await _http.GetAsync($"{_gateway.BaseUrl}/metadata/search?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }
}
