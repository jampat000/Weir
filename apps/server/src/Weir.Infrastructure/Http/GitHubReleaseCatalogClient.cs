using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using Weir.Core.Configuration;
using Weir.Core.Json;
using Weir.Core.Updates;
using Weir.Infrastructure.Runtime;

namespace Weir.Infrastructure.Http;

/// <summary>
/// Finds the newest release on GitHub while spending as little of its unauthenticated allowance as possible: the last answer's
/// <c>ETag</c> goes back as <c>If-None-Match</c> so an unchanged list (a 304) is free and is served from the stored copy, which
/// is kept in <c>WEIR_HOME</c> so a restart does not lose it. When GitHub limits the network (403 or 429 with no allowance
/// left, or a <c>Retry-After</c>) the API is left alone until it says the limit lifts, and the public release feed answers in
/// the meantime, as that does not count against the allowance.
/// </summary>
public sealed class GitHubReleaseCatalogClient : IReleaseCatalogClient
{
    private const string CacheFileName = "release-cache.json";
    private const long MostFeedBytes = 2_000_000;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LeastWait = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MostWait = TimeSpan.FromHours(1);
    private static readonly long MostUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    private readonly WeirOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<GitHubReleaseCatalogClient> _logger;
    private readonly HttpMessageHandler _handler;
    private readonly Lock _gate = new();
    private bool _cacheLoaded;
    private string? _etag;
    private IReadOnlyList<GitHubReleaseRecord>? _releases;
    private (int Status, DateTimeOffset ResetsAt)? _limit;

    /// <summary>The <paramref name="handler"/> carries the requests; the default connects to loopback first and follows redirects.</summary>
    public GitHubReleaseCatalogClient(WeirOptions options, TimeProvider time, ILogger<GitHubReleaseCatalogClient> logger, HttpMessageHandler? handler = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _handler = handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            ConnectCallback = LoopbackFirstConnect.ConnectAsync,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
    }

    public async Task<GitHubReleaseRecord?> FetchLatestAsync(string currentVersion, CancellationToken cancellationToken)
    {
        var (etag, releases, limit) = Known();
        if (limit is { } active && active.ResetsAt > _time.GetUtcNow())
        {
            return await FromFeedAsync(currentVersion, active.Status, active.ResetsAt, releases, cancellationToken).ConfigureAwait(false);
        }

        using var client = new HttpClient(_handler, disposeHandler: false) { Timeout = RequestTimeout };
        using var request = Request(ReleaseCatalog.ReleasesUrl, currentVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (etag is not null && releases is not null && EntityTagHeaderValue.TryParse(etag, out var entityTag))
        {
            request.Headers.IfNoneMatch.Add(entityTag);
        }

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified && releases is not null)
        {
            Remember(etag, releases);
            return ReleaseSelection.NewestFor(releases, currentVersion);
        }

        var status = (int)response.StatusCode;
        if (status >= 400)
        {
            if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
            {
                throw new ReleaseFetchException(status);
            }

            var resetsAt = ResetOf(response);
            if (resetsAt is { } limitedUntil)
            {
                lock (_gate)
                {
                    _limit = (status, limitedUntil);
                }
            }

            return await FromFeedAsync(currentVersion, status, resetsAt, releases, cancellationToken).ConfigureAwait(false);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var fetched = ReleaseCatalog.CoerceReleaseListPayload(WireJsonParser.ParseBytes(bytes));
        var fetchedEtag = response.Headers.ETag?.ToString();
        Remember(fetchedEtag, fetched);
        if (fetchedEtag is not null)
        {
            Save(fetchedEtag, bytes);
        }

        return ReleaseSelection.NewestFor(fetched, currentVersion);
    }

    private static HttpRequestMessage Request(string url, string currentVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", $"Weir/{currentVersion}");
        return request;
    }

    /// <summary>
    /// The newest release from the public feed. When the feed cannot answer either, the failure is GitHub's: a limit when its
    /// end is known (with the newest release the API last gave), otherwise the API's own error status.
    /// </summary>
    private async Task<GitHubReleaseRecord?> FromFeedAsync(
        string currentVersion, int status, DateTimeOffset? resetsAt, IReadOnlyList<GitHubReleaseRecord>? known, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient(_handler, disposeHandler: false) { Timeout = RequestTimeout, MaxResponseContentBufferSize = MostFeedBytes };
            using var request = Request(ReleaseCatalog.ReleasesFeedUrl, currentVersion);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var feed = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ReleaseSelection.NewestFor(WithApiRecords(ReleaseFeed.Parse(feed), known), currentVersion);
        }
        catch (Exception exception) when (exception is HttpRequestException or XmlException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw resetsAt is { } until
                ? new ReleaseFetchException(status, new ReleaseRateLimit(until, known is null ? null : ReleaseSelection.NewestFor(known, currentVersion)))
                : new ReleaseFetchException(status);
        }
    }

    /// <summary>
    /// The feed's releases, each replaced by the API's own record of it when the API has given one: the feed cannot say whether
    /// GitHub marks a release as a pre-release or a draft, the API can.
    /// </summary>
    private static List<GitHubReleaseRecord> WithApiRecords(IReadOnlyList<GitHubReleaseRecord> fromFeed, IReadOnlyList<GitHubReleaseRecord>? known) =>
        [.. fromFeed.Select(release => known?.FirstOrDefault(record => record.TagName == release.TagName) ?? release)];

    /// <summary>
    /// When GitHub says the limit lifts: <c>Retry-After</c> when sent, otherwise <c>X-RateLimit-Reset</c> once no allowance is
    /// left; null when the response is not a limit. Both are worked out as a wait from GitHub's own <c>Date</c>, so a clock
    /// that is off here changes nothing, and kept between a minute and an hour from now, so an odd value never makes Weir
    /// hammer GitHub or give up on it.
    /// </summary>
    private DateTimeOffset? ResetOf(HttpResponseMessage response)
    {
        var now = _time.GetUtcNow();
        var theirNow = response.Headers.Date ?? now;
        var wait = RetryAfterWait(response, theirNow) ?? ResetWait(response, theirNow);
        return wait is { } until ? now + (until < LeastWait ? LeastWait : until > MostWait ? MostWait : until) : null;
    }

    /// <summary>How long <c>Retry-After</c> asks to wait; a date in it is measured from GitHub's own clock, as sent in <c>Date</c>.</summary>
    private static TimeSpan? RetryAfterWait(HttpResponseMessage response, DateTimeOffset theirNow) =>
        response.Headers.RetryAfter is { } after ? after.Delta ?? (after.Date - theirNow) : null;

    /// <summary>How long until <c>X-RateLimit-Reset</c> once no allowance is left; null for a missing or impossible value.</summary>
    private static TimeSpan? ResetWait(HttpResponseMessage response, DateTimeOffset theirNow) =>
        HeaderOf(response, "X-RateLimit-Remaining") == "0"
        && long.TryParse(HeaderOf(response, "X-RateLimit-Reset"), CultureInfo.InvariantCulture, out var epoch)
        && epoch is >= 0 && epoch <= MostUnixSeconds
            ? DateTimeOffset.FromUnixTimeSeconds(epoch) - theirNow
            : null;

    private static string? HeaderOf(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault()?.Trim() : null;

    private (string? Etag, IReadOnlyList<GitHubReleaseRecord>? Releases, (int Status, DateTimeOffset ResetsAt)? Limit) Known()
    {
        lock (_gate)
        {
            if (!_cacheLoaded)
            {
                _cacheLoaded = true;
                Load();
            }

            return (_etag, _releases, _limit);
        }
    }

    private void Remember(string? etag, IReadOnlyList<GitHubReleaseRecord> releases)
    {
        lock (_gate)
        {
            _etag = etag;
            _releases = releases;
            _limit = null;
        }
    }

    /// <summary>Reads the copy saved by an earlier run; one that cannot be read is dropped, and the next answer replaces it.</summary>
    private void Load()
    {
        try
        {
            if (WireJsonParser.ParseBytes(File.ReadAllBytes(Path.Join(_options.WeirHome, CacheFileName))) is WireObject saved
                && saved.Get("etag") is WireString etag
                && saved.Get("releases") is { } releases)
            {
                _releases = ReleaseCatalog.CoerceReleaseListPayload(releases);
                _etag = EntityTagHeaderValue.TryParse(etag.Value, out _) ? etag.Value : null;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or WireJsonDecodeException or WireValueException or WireTypeException or FormatException)
        {
            _logger.LogDebug(exception, "The saved release list could not be read, so the next check asks GitHub for it again.");
        }
    }

    private void Save(string etag, byte[] body)
    {
        try
        {
            var saved = new WireObject().Set("etag", etag).Set("releases", WireJsonParser.ParseBytes(body));
            AtomicFileWriter.Replace(_options.WeirHome, CacheFileName, Encoding.UTF8.GetBytes(WireJsonWriter.Dumps(saved, WireJsonFormat.Compact)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(exception, "The release list could not be saved, so a restart asks GitHub for it again.");
        }
    }
}
