using System.Net;
using System.Net.Http.Headers;
using Weir.Infrastructure.Artwork;
using Weir.Infrastructure.Tests.MediaManagers;

namespace Weir.Infrastructure.Tests.Artwork;

/// <summary>How Weir reads the metadata service's answers, and what it refuses to take from them.</summary>
public sealed class ArtworkGatewayClientTests
{
    private static ArtworkLookup Film(string title = "nosferatu", int? year = 1922) => new("k", "movie", title, year, null, null, null, 0, ArtworkOutcomes.Pending, null, true);

    private static string Result(string? posterUrl, string providerId, string? originalLanguage = null) =>
        $$"""{"provider":"tmdb","providerId":"{{providerId}}","originalLanguage":{{(originalLanguage is null ? "null" : $"\"{originalLanguage}\"")}},"posterUrl":{{(posterUrl is null ? "null" : $"\"{posterUrl}\"")}}}""";

    [Fact]
    public async Task The_first_result_that_has_a_poster_is_the_match()
    {
        using var fixture = new ArtworkFixture();
        fixture.ServeSearch($$"""{"results":[{{Result(null, "1")}},{{Result($"{ArtworkFixture.GatewayUrl}/artwork/w780/second.jpg", "2")}},{{Result($"{ArtworkFixture.GatewayUrl}/artwork/w780/third.jpg", "3")}}]}""");

        var answer = await fixture.Gateway.FindAsync(Film(), CancellationToken.None);

        Assert.Equal(GatewayStatus.Ok, answer.Status);
        Assert.Equal(("second.jpg", 2L), (answer.Value!.PosterRef, answer.Value.TmdbId));
    }

    [Fact]
    public async Task A_poster_address_on_a_host_weir_does_not_fetch_from_is_not_used()
    {
        using var fixture = new ArtworkFixture();
        fixture.ServeSearch($$"""{"results":[{{Result("https://evil.example/artwork/w780/second.jpg", "2")}}]}""");

        var answer = await fixture.Gateway.FindAsync(Film(), CancellationToken.None);

        Assert.Null(answer.Value!.PosterRef);
    }

    [Fact]
    public async Task The_original_language_comes_from_the_same_result_as_the_poster_in_lower_case()
    {
        using var fixture = new ArtworkFixture();
        fixture.ServeSearch($$"""{"results":[{{Result(null, "1", "ja")}},{{Result($"{ArtworkFixture.GatewayUrl}/artwork/w780/second.jpg", "2", " FR ")}}]}""");

        var answer = await fixture.Gateway.FindAsync(Film(), CancellationToken.None);

        Assert.Equal(("second.jpg", "fr"), (answer.Value!.PosterRef, answer.Value.OriginalLanguage));
    }

    [Fact]
    public async Task When_no_result_has_a_poster_the_first_results_language_is_still_reported()
    {
        using var fixture = new ArtworkFixture();
        fixture.ServeSearch($$"""{"results":[{{Result(null, "1", "ja")}},{{Result(null, "2", "fr")}}]}""");

        var answer = await fixture.Gateway.FindAsync(Film(), CancellationToken.None);

        Assert.Equal((null, "ja"), (answer.Value!.PosterRef, answer.Value.OriginalLanguage));
    }

    [Fact]
    public async Task A_result_that_names_no_language_reports_none()
    {
        using var fixture = new ArtworkFixture();
        fixture.ServeSearch($$"""{"results":[{{Result($"{ArtworkFixture.GatewayUrl}/artwork/w780/a.jpg", "1")}}]}""");

        var answer = await fixture.Gateway.FindAsync(Film(), CancellationToken.None);

        Assert.Null(answer.Value!.OriginalLanguage);
    }

    [Fact]
    public async Task A_series_known_only_by_its_tvdb_id_reports_the_language_thetvdb_gives()
    {
        using var fixture = new ArtworkFixture();
        fixture.Http.Json(HttpMethod.Get, "/metadata/tvdb/tv/78804", """{"provider":"tvdb","series":{"tvdbId":"78804","originalLanguage":"eng","posterUrl":null}}""");
        var series = new ArtworkLookup("k", "tv", string.Empty, null, null, 78804, null, 0, ArtworkOutcomes.Pending, null, true);

        var answer = await fixture.Gateway.FindAsync(series, CancellationToken.None);

        Assert.Equal("eng", answer.Value!.OriginalLanguage);
    }

    [Fact]
    public async Task A_service_that_answers_its_health_check_is_reachable()
    {
        using var fixture = new ArtworkFixture();
        fixture.Http.Json(HttpMethod.Get, "/health", """{"service":"deluno-metadata-gateway","status":"ok"}""");

        Assert.Equal(GatewayStatus.Ok, await fixture.Gateway.CheckHealthAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_health_check_that_does_not_say_ok_is_not_reachable()
    {
        using var fixture = new ArtworkFixture();
        fixture.Http.Json(HttpMethod.Get, "/health", """{"status":"degraded"}""");

        Assert.Equal(GatewayStatus.Unavailable, await fixture.Gateway.CheckHealthAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_service_that_cannot_be_reached_fails_its_health_check()
    {
        using var fixture = new ArtworkFixture();

        Assert.Equal(GatewayStatus.Unavailable, await fixture.Gateway.CheckHealthAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_title_is_sent_url_encoded_with_nothing_else_about_the_person()
    {
        using var fixture = new ArtworkFixture();
        fixture.ServeSearch("""{"results":[]}""");

        await fixture.Gateway.FindAsync(Film("tom & jerry: the movie", null), CancellationToken.None);

        var request = Assert.Single(fixture.Searches());
        Assert.Equal("tom & jerry: the movie", ArtworkFixture.QueryValue(request, "query"));
        Assert.DoesNotContain("year", request.Uri.Query, StringComparison.Ordinal);
        Assert.Equal(["mediaType", "query"], System.Web.HttpUtility.ParseQueryString(request.Uri.Query).AllKeys.OfType<string>());
        Assert.Contains("Weir/", request.Headers["User-Agent"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_answer_that_is_not_json_is_not_found_rather_than_an_error()
    {
        using var fixture = new ArtworkFixture();
        fixture.ServeSearch("<html>not json</html>");

        var answer = await fixture.Gateway.FindAsync(Film(), CancellationToken.None);

        Assert.Equal(GatewayStatus.NotFound, answer.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, GatewayStatus.NotFound)]
    [InlineData(HttpStatusCode.BadRequest, GatewayStatus.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable, GatewayStatus.Busy)]
    [InlineData(HttpStatusCode.TooManyRequests, GatewayStatus.Busy)]
    [InlineData(HttpStatusCode.BadGateway, GatewayStatus.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, GatewayStatus.Unavailable)]
    public async Task The_status_the_service_answers_with_says_whether_to_try_again(HttpStatusCode status, GatewayStatus expected)
    {
        using var fixture = new ArtworkFixture();
        fixture.Http.Json(HttpMethod.Get, "/metadata/search", "{}", status);

        var answer = await fixture.Gateway.FindAsync(Film(), CancellationToken.None);

        Assert.Equal(expected, answer.Status);
    }

    [Fact]
    public async Task A_reply_that_is_not_an_image_is_not_stored_as_a_poster()
    {
        using var fixture = new ArtworkFixture();
        fixture.Http.Route(HttpMethod.Get, "/artwork/w342/a.jpg", _ => FakeManagerHttp.Response(HttpStatusCode.OK, "<html></html>"));

        var answer = await fixture.Gateway.FetchPosterAsync("a.jpg", CancellationToken.None);

        Assert.Equal(GatewayStatus.NotFound, answer.Status);
    }

    [Fact]
    public async Task An_image_larger_than_the_limit_is_not_taken()
    {
        using var fixture = new ArtworkFixture();
        fixture.Http.Route(HttpMethod.Get, "/artwork/w342/a.jpg", _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[6 * 1024 * 1024]) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return response;
        });

        var answer = await fixture.Gateway.FetchPosterAsync("a.jpg", CancellationToken.None);

        Assert.Equal(GatewayStatus.NotFound, answer.Status);
    }

    [Fact]
    public async Task A_missing_retry_after_is_reported_as_no_wait()
    {
        using var fixture = new ArtworkFixture();
        fixture.Http.Json(HttpMethod.Get, "/metadata/search", "{}", HttpStatusCode.ServiceUnavailable);

        var answer = await fixture.Gateway.FindAsync(Film(), CancellationToken.None);

        Assert.Null(answer.RetryAfter);
    }

    [Fact]
    public async Task A_retry_after_in_seconds_is_read()
    {
        using var fixture = new ArtworkFixture();
        fixture.Http.Route(HttpMethod.Get, "/metadata/search", _ => FakeManagerHttp.Response(HttpStatusCode.ServiceUnavailable, "{}", ("Retry-After", "45")));

        var answer = await fixture.Gateway.FindAsync(Film(), CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(45), answer.RetryAfter);
    }
}
