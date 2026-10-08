using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Weir.Core.Updates;
using Weir.Infrastructure.Http;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.Http;

/// <summary>Looking up the newest release without wasting GitHub's allowance, and carrying on when the allowance is gone.</summary>
public sealed class GitHubReleaseCatalogClientTests : IDisposable
{
    private const string Running = "3.2.16";
    private readonly StoreFixture _fixture = new(("WEIR_VERSION", Running));
    private readonly FakeGitHub _github = new();
    private readonly GitHubReleaseCatalogClient _client;

    public GitHubReleaseCatalogClientTests()
    {
        _client = NewClient();
    }

    public void Dispose() => _fixture.Dispose();

    private GitHubReleaseCatalogClient NewClient() =>
        new(_fixture.Options, _fixture.Clock, NullLogger<GitHubReleaseCatalogClient>.Instance, _github);

    private static string ReleaseList(string version, bool preRelease = false) =>
        $$"""
        [{"tag_name":"v{{version}}","name":"Weir {{version}}","html_url":"https://github.com/jampat000/Weir/releases/tag/v{{version}}",
          "published_at":"2026-10-01T10:00:00Z","draft":false,"prerelease":{{(preRelease ? "true" : "false")}},
          "assets":[{"name":"Weir-win-Setup.exe","url":"https://api.github.com/repos/jampat000/Weir/releases/assets/1",
                     "browser_download_url":"https://github.com/jampat000/Weir/releases/download/v{{version}}/Weir-win-Setup.exe",
                     "size":1000,"content_type":"application/octet-stream"}]}]
        """;

    private static string Feed(params string[] tags) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><feed xmlns=\"http://www.w3.org/2005/Atom\">"
        + string.Concat(tags.Select(tag =>
            $"<entry><id>tag:github.com,2008:Repository/1/{tag}</id><updated>2026-10-05T08:30:00Z</updated>"
            + $"<link rel=\"alternate\" type=\"text/html\" href=\"https://github.com/jampat000/Weir/releases/tag/{tag}\"/><title>Weir {tag}</title></entry>"))
        + "</feed>";

    private static HttpResponseMessage Json(string body, string? etag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (etag is not null)
        {
            response.Headers.TryAddWithoutValidation("ETag", etag);
        }

        return response;
    }

    private static HttpResponseMessage Limited(HttpStatusCode status, DateTimeOffset resetsAt, DateTimeOffset? githubNow = null)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Date = githubNow;
        response.Headers.Add("X-RateLimit-Remaining", "0");
        response.Headers.Add("X-RateLimit-Reset", resetsAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        return response;
    }

    [Fact]
    public async Task An_unchanged_list_is_asked_for_with_its_etag_and_answered_from_the_stored_copy()
    {
        _github.Api = request => request.Headers.IfNoneMatch.Count == 0
            ? Json(ReleaseList("3.3.0"), "\"abc\"")
            : new HttpResponseMessage(HttpStatusCode.NotModified);

        var first = await _client.FetchLatestAsync(Running, CancellationToken.None);
        var second = await _client.FetchLatestAsync(Running, CancellationToken.None);

        Assert.Equal("3.3.0", first?.Version);
        Assert.Equal(first, second);
        Assert.Equal([null, "\"abc\""], _github.ApiIfNoneMatch);
    }

    [Fact]
    public async Task The_stored_copy_survives_a_restart()
    {
        _github.Api = request => request.Headers.IfNoneMatch.Count == 0
            ? Json(ReleaseList("3.3.0"), "\"abc\"")
            : new HttpResponseMessage(HttpStatusCode.NotModified);
        await _client.FetchLatestAsync(Running, CancellationToken.None);

        var restarted = await NewClient().FetchLatestAsync(Running, CancellationToken.None);

        Assert.Equal("3.3.0", restarted?.Version);
        Assert.Equal([null, "\"abc\""], _github.ApiIfNoneMatch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("\"unterminated")]
    public async Task A_stored_etag_that_is_not_one_is_not_sent_and_the_check_still_goes_through(string saved)
    {
        File.WriteAllText(
            Path.Join(_fixture.Home.Path, "release-cache.json"),
            "{\"etag\":" + System.Text.Json.JsonSerializer.Serialize(saved) + ",\"releases\":" + ReleaseList("3.3.0") + "}");
        _github.Api = _ => Json(ReleaseList("3.4.0"), "\"def\"");

        var latest = await NewClient().FetchLatestAsync(Running, CancellationToken.None);

        Assert.Equal("3.4.0", latest?.Version);
        Assert.Equal([null], _github.ApiIfNoneMatch);
    }

    [Fact]
    public async Task A_list_that_changed_replaces_the_stored_copy()
    {
        var version = "3.3.0";
        _github.Api = request => Json(ReleaseList(version), $"\"{version}\"");
        await _client.FetchLatestAsync(Running, CancellationToken.None);

        version = "3.4.0";
        var changed = await _client.FetchLatestAsync(Running, CancellationToken.None);

        Assert.Equal("3.4.0", changed?.Version);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_limit_leaves_the_api_alone_until_it_lifts_and_the_public_feed_answers_meanwhile(HttpStatusCode refusal)
    {
        var resetsAt = _fixture.Clock.GetUtcNow().AddMinutes(20);
        _github.Api = _ => Limited(refusal, resetsAt);
        _github.PublicFeed = _ => Json(Feed("v3.4.0", "v3.3.0", "untagged-1b2c3d", "nightly"));

        var during = await _client.FetchLatestAsync(Running, CancellationToken.None);
        _fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        var later = await _client.FetchLatestAsync(Running, CancellationToken.None);

        Assert.Equal("3.4.0", during?.Version);
        Assert.Equal("https://github.com/jampat000/Weir/releases/tag/v3.4.0", during?.HtmlUrl);
        Assert.Null(during?.WindowsInstallerAsset());
        Assert.Equal("3.4.0", later?.Version);
        Assert.Equal(1, _github.ApiCalls);
        Assert.Equal(2, _github.FeedCalls);

        _github.Api = _ => Json(ReleaseList("3.5.0"));
        _fixture.Clock.Advance(TimeSpan.FromMinutes(11));
        var after = await _client.FetchLatestAsync(Running, CancellationToken.None);

        Assert.Equal("3.5.0", after?.Version);
        Assert.Equal(2, _github.ApiCalls);
    }

    [Fact]
    public async Task When_the_feed_fails_too_the_failure_is_the_limit_with_the_last_release_the_api_gave()
    {
        _github.Api = request => request.Headers.IfNoneMatch.Count == 0 ? Json(ReleaseList("3.3.0"), "\"abc\"") : Retry(HttpStatusCode.Forbidden, seconds: 120);
        _github.PublicFeed = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        await _client.FetchLatestAsync(Running, CancellationToken.None);

        var failure = await Assert.ThrowsAsync<ReleaseFetchException>(() => _client.FetchLatestAsync(Running, CancellationToken.None));
        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var again = await Assert.ThrowsAsync<ReleaseFetchException>(() => _client.FetchLatestAsync(Running, CancellationToken.None));

        Assert.Equal(403, failure.StatusCode);
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 10, 2, 0, TimeSpan.Zero), failure.RateLimit?.ResetsAt);
        Assert.Equal("3.3.0", failure.RateLimit?.LastKnown?.Version);
        Assert.Equal(failure.RateLimit, again.RateLimit);
        Assert.Equal(2, _github.ApiCalls);
    }

    [Theory]
    [InlineData(-1000, 1)]
    [InlineData(86400, 60)]
    public async Task A_reset_that_makes_no_sense_is_kept_between_a_minute_and_an_hour(long resetAfterSeconds, int expectedMinutes)
    {
        var now = _fixture.Clock.GetUtcNow();
        _github.Api = _ => Limited(HttpStatusCode.Forbidden, now.AddSeconds(resetAfterSeconds));
        _github.PublicFeed = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var failure = await Assert.ThrowsAsync<ReleaseFetchException>(() => _client.FetchLatestAsync(Running, CancellationToken.None));

        Assert.Equal(now.AddMinutes(expectedMinutes), failure.RateLimit?.ResetsAt);
    }

    [Fact]
    public async Task A_reset_is_a_wait_measured_on_githubs_clock_so_a_fast_local_clock_does_not_shorten_it()
    {
        var now = _fixture.Clock.GetUtcNow();
        var githubNow = now.AddMinutes(-10);
        _github.Api = _ => Limited(HttpStatusCode.Forbidden, githubNow.AddMinutes(20), githubNow);
        _github.PublicFeed = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var failure = await Assert.ThrowsAsync<ReleaseFetchException>(() => _client.FetchLatestAsync(Running, CancellationToken.None));

        Assert.Equal(now.AddMinutes(20), failure.RateLimit?.ResetsAt);
    }

    [Fact]
    public async Task A_retry_after_date_is_measured_on_githubs_clock_too()
    {
        var now = _fixture.Clock.GetUtcNow();
        var githubNow = now.AddMinutes(10);
        _github.Api = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.Date = githubNow;
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(githubNow.AddMinutes(15));
            return response;
        };
        _github.PublicFeed = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var failure = await Assert.ThrowsAsync<ReleaseFetchException>(() => _client.FetchLatestAsync(Running, CancellationToken.None));

        Assert.Equal(now.AddMinutes(15), failure.RateLimit?.ResetsAt);
    }

    [Fact]
    public async Task A_reset_too_large_to_be_a_time_leaves_the_feed_to_answer()
    {
        _github.Api = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            response.Headers.Add("X-RateLimit-Remaining", "0");
            response.Headers.Add("X-RateLimit-Reset", "99999999999999999");
            return response;
        };
        _github.PublicFeed = _ => Json(Feed("v3.3.0"));

        var latest = await _client.FetchLatestAsync(Running, CancellationToken.None);

        Assert.Equal("3.3.0", latest?.Version);
    }

    [Fact]
    public async Task A_release_the_api_marked_as_a_pre_release_stays_one_when_the_feed_answers()
    {
        var limited = false;
        _github.Api = request => limited ? Limited(HttpStatusCode.Forbidden, _fixture.Clock.GetUtcNow().AddMinutes(20)) : Json(ReleaseList("3.3.0", preRelease: true), "\"abc\"");
        _github.PublicFeed = _ => Json(Feed("v3.3.0", "v3.2.17"));
        await _client.FetchLatestAsync(Running, CancellationToken.None);
        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        limited = true;

        var latest = await _client.FetchLatestAsync(Running, CancellationToken.None);

        Assert.Equal("3.2.17", latest?.Version);
    }

    [Fact]
    public async Task A_refusal_that_is_not_a_limit_is_an_error_status_unless_the_feed_answers()
    {
        _github.Api = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);
        _github.PublicFeed = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var failure = await Assert.ThrowsAsync<ReleaseFetchException>(() => _client.FetchLatestAsync(Running, CancellationToken.None));

        Assert.Equal(403, failure.StatusCode);
        Assert.Null(failure.RateLimit);

        _github.PublicFeed = _ => Json(Feed("v3.3.0"));
        Assert.Equal("3.3.0", (await _client.FetchLatestAsync(Running, CancellationToken.None))?.Version);
    }

    [Fact]
    public async Task Any_other_error_status_is_not_answered_from_the_feed()
    {
        _github.Api = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var failure = await Assert.ThrowsAsync<ReleaseFetchException>(() => _client.FetchLatestAsync(Running, CancellationToken.None));

        Assert.Equal(500, failure.StatusCode);
        Assert.Equal(0, _github.FeedCalls);
    }

    [Fact]
    public async Task The_feed_offers_a_pre_release_only_to_a_pre_release_install()
    {
        _github.Api = _ => Limited(HttpStatusCode.Forbidden, _fixture.Clock.GetUtcNow().AddMinutes(20));
        _github.PublicFeed = _ => Json(Feed("v3.3.0-rc.2", "v3.2.17"));

        var stable = await _client.FetchLatestAsync(Running, CancellationToken.None);
        var preRelease = await _client.FetchLatestAsync("3.3.0-rc.1", CancellationToken.None);

        Assert.Equal("3.2.17", stable?.Version);
        Assert.Equal("3.3.0-rc.2", preRelease?.Version);
    }

    private static HttpResponseMessage Retry(HttpStatusCode status, int seconds)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        return response;
    }

    /// <summary>GitHub's API and public release feed, answering as the test says and counting what was asked.</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        private readonly List<string?> _ifNoneMatch = [];
        private int _apiCalls;
        private int _feedCalls;

        public Func<HttpRequestMessage, HttpResponseMessage> Api { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        public Func<HttpRequestMessage, HttpResponseMessage> PublicFeed { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        public int ApiCalls => Volatile.Read(ref _apiCalls);

        public int FeedCalls => Volatile.Read(ref _feedCalls);

        public IReadOnlyList<string?> ApiIfNoneMatch => _ifNoneMatch;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host == "api.github.com")
            {
                Interlocked.Increment(ref _apiCalls);
                _ifNoneMatch.Add(request.Headers.IfNoneMatch.FirstOrDefault()?.ToString());
                return Task.FromResult(Api(request));
            }

            Assert.Equal(ReleaseCatalog.ReleasesFeedUrl, request.RequestUri?.ToString());
            Interlocked.Increment(ref _feedCalls);
            return Task.FromResult(PublicFeed(request));
        }
    }
}
