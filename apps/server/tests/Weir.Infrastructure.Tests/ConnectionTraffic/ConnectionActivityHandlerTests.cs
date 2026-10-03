using System.Net;
using Microsoft.Extensions.Time.Testing;
using Weir.Core.MediaManagers;
using Weir.Infrastructure.ConnectionTraffic;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Tests.MediaManagers;

namespace Weir.Infrastructure.Tests.ConnectionTraffic;

/// <summary>
/// Every call a media manager or download client client makes is reported from the one transport they share: asked, then
/// answered or failed with how long it took. A call with no saved connection behind it reports nothing.
/// </summary>
public sealed class ConnectionActivityHandlerTests
{
    private static readonly ConnectionRef Radarr = new(ConnectionKind.MediaManager, 7);
    private static readonly ConnectionRef QBittorrent = new(ConnectionKind.DownloadClient, 3);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly ConnectionActivityHub _hub;
    private readonly ConnectionActivitySubscription _stream;

    public ConnectionActivityHandlerTests()
    {
        _hub = new ConnectionActivityHub(_time);
        _stream = _hub.Subscribe();
    }

    private async Task<List<ConnectionActivity>> HeardAsync()
    {
        _stream.Dispose();
        var heard = new List<ConnectionActivity>();
        await foreach (var activity in _stream.ReadAllAsync(CancellationToken.None))
        {
            heard.Add(activity);
        }

        return heard;
    }

    private FakeManagerHttp Manager(string path, HttpStatusCode status, TimeSpan takes) =>
        new FakeManagerHttp(_hub, _time).Route(HttpMethod.Get, path, _ =>
        {
            _time.Advance(takes);
            return FakeManagerHttp.Response(status, "{}");
        });

    [Fact]
    public async Task A_call_to_a_saved_manager_is_reported_as_asked_and_then_answered_with_how_long_it_took()
    {
        var http = Manager("/api/v3/system/status", HttpStatusCode.OK, TimeSpan.FromMilliseconds(84));
        var client = new MediaManagerHttpClient("http://127.0.0.1:7878", "key", http, connection: Radarr);

        await client.GetJsonAsync("/api/v3/system/status");

        var heard = await HeardAsync();
        Assert.Equal([ConnectionPhase.Asked, ConnectionPhase.Answered], heard.Select(activity => activity.Phase));
        Assert.All(heard, activity => Assert.Equal(Radarr, activity.Connection));
        Assert.All(heard, activity => Assert.Equal(ConnectionDirection.Outbound, activity.Direction));
        Assert.Equal([null, 84L], heard.Select(activity => activity.Milliseconds));
    }

    [Fact]
    public async Task A_call_to_a_saved_download_client_is_reported_against_that_client()
    {
        var http = Manager("/api/v2/app/version", HttpStatusCode.OK, TimeSpan.FromMilliseconds(12));
        var client = new DownloadClientHttpClient("http://127.0.0.1:8080", http, connection: QBittorrent);

        await client.SendAsync(HttpMethod.Get, "/api/v2/app/version");

        var heard = await HeardAsync();
        Assert.Equal([ConnectionPhase.Asked, ConnectionPhase.Answered], heard.Select(activity => activity.Phase));
        Assert.All(heard, activity => Assert.Equal(QBittorrent, activity.Connection));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_broken_manager_or_a_refused_key_is_reported_as_failed(HttpStatusCode status)
    {
        var http = Manager("/api/v3/system/status", status, TimeSpan.FromMilliseconds(30));
        var client = new MediaManagerHttpClient("http://127.0.0.1:7878", "key", http, connection: Radarr);

        await Assert.ThrowsAsync<MediaManagerHttpException>(() => client.GetJsonAsync("/api/v3/system/status"));

        var heard = await HeardAsync();
        Assert.Equal([ConnectionPhase.Asked, ConnectionPhase.Failed], heard.Select(activity => activity.Phase));
        Assert.Equal(30, heard[1].Milliseconds);
    }

    [Fact]
    public async Task A_not_found_about_one_title_is_still_an_answer_from_the_manager()
    {
        var http = Manager("/api/v3/movie/9", HttpStatusCode.NotFound, TimeSpan.FromMilliseconds(5));
        var client = new MediaManagerHttpClient("http://127.0.0.1:7878", "key", http, connection: Radarr);

        await Assert.ThrowsAsync<MediaManagerHttpException>(() => client.GetJsonAsync("/api/v3/movie/9"));

        Assert.Equal([ConnectionPhase.Asked, ConnectionPhase.Answered], (await HeardAsync()).Select(activity => activity.Phase));
    }

    [Fact]
    public async Task A_manager_that_cannot_be_reached_is_reported_as_failed_and_the_failure_still_reaches_the_caller()
    {
        var http = new FakeManagerHttp(_hub, _time).Route(HttpMethod.Get, "/api/v3/system/status", _ =>
        {
            _time.Advance(TimeSpan.FromSeconds(10));
            throw new HttpRequestException("refused");
        });
        var client = new MediaManagerHttpClient("http://127.0.0.1:7878", "key", http, connection: Radarr);

        await Assert.ThrowsAsync<MediaManagerUnreachableException>(() => client.GetJsonAsync("/api/v3/system/status"));

        var heard = await HeardAsync();
        Assert.Equal([ConnectionPhase.Asked, ConnectionPhase.Failed], heard.Select(activity => activity.Phase));
        Assert.Equal(10_000, heard[1].Milliseconds);
    }

    [Fact]
    public async Task A_call_with_no_saved_connection_behind_it_reports_nothing()
    {
        var http = Manager("/api/v3/system/status", HttpStatusCode.OK, TimeSpan.FromMilliseconds(5));
        var client = new MediaManagerHttpClient("http://127.0.0.1:7878", "key", http);

        await client.GetJsonAsync("/api/v3/system/status");

        Assert.Empty(await HeardAsync());
    }

    [Fact]
    public async Task Every_kind_of_manager_call_is_reported_not_only_reads()
    {
        var http = new FakeManagerHttp(_hub, _time)
            .Json(HttpMethod.Post, "/api/v3/command", "{}")
            .Json(HttpMethod.Delete, "/api/v3/queue/4", "");
        var client = new MediaManagerHttpClient("http://127.0.0.1:7878", "key", http, connection: Radarr);

        await client.PostJsonAsync("/api/v3/command", new Weir.Core.Json.WireObject());
        _time.Advance(ConnectionActivityHub.Throttle);
        await client.DeleteAsync("/api/v3/queue/4");

        var heard = await HeardAsync();
        Assert.Equal(4, heard.Count);
        Assert.Equal([ConnectionPhase.Asked, ConnectionPhase.Answered, ConnectionPhase.Asked, ConnectionPhase.Answered], heard.Select(activity => activity.Phase));
    }
}
