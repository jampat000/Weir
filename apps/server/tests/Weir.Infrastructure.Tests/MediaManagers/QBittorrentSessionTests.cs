using Weir.Core.MediaManagers;
using Weir.Infrastructure.MediaManagers;

namespace Weir.Infrastructure.Tests.MediaManagers;

/// <summary>
/// qBittorrent sessions over the production handler factory against a server that answers as 5.x does (#856):
/// a login that carries a live session cookie is answered 204 without a new one, so Weir must never send one.
/// </summary>
public sealed class QBittorrentSessionTests : IDisposable
{
    private readonly SocketsManagerHttpHandlerFactory _handlers = new();
    private readonly FakeQBittorrentServer _server = new();

    public void Dispose()
    {
        _server.Dispose();
        _handlers.Dispose();
    }

    private static DownloadClientConnection Connect(FakeQBittorrentServer server, string password = "right") =>
        new(DownloadClientKinds.QBittorrent, "qBittorrent", server.BaseUrl, "deluno", password, null, 1);

    [Fact]
    public async Task Repeated_tests_of_the_same_qBittorrent_all_connect()
    {
        var port = new QBittorrentPort(_handlers);

        var first = await port.TestAsync(Connect(_server));
        var second = await port.TestAsync(Connect(_server));
        var third = await port.TestAsync(Connect(_server));

        Assert.True(first.Ok, first.Detail);
        Assert.True(second.Ok, second.Detail);
        Assert.True(third.Ok, third.Detail);
    }

    [Fact]
    public async Task Every_login_is_sent_without_a_session_cookie()
    {
        var port = new QBittorrentPort(_handlers);

        await port.TestAsync(Connect(_server));
        await port.ReadFoldersAsync(Connect(_server));
        await port.TestAsync(Connect(_server));

        Assert.Equal(3, _server.LoginCookieHeaders.Count);
        Assert.All(_server.LoginCookieHeaders, Assert.Null);
    }

    [Fact]
    public async Task A_qBittorrent_that_forgot_its_session_still_connects()
    {
        var port = new QBittorrentPort(_handlers);
        await port.TestAsync(Connect(_server));
        _server.ExpireSessions();

        var (ok, detail) = await port.TestAsync(Connect(_server));

        Assert.True(ok, detail);
    }

    [Fact]
    public async Task A_session_cookie_from_one_qBittorrent_is_not_sent_to_another_on_the_same_host()
    {
        using var other = new FakeQBittorrentServer();
        var port = new QBittorrentPort(_handlers);

        await port.TestAsync(Connect(_server));
        var (ok, detail) = await port.TestAsync(Connect(other));

        Assert.True(ok, detail);
        Assert.Null(Assert.Single(other.LoginCookieHeaders));
    }

    [Fact]
    public async Task A_wrong_password_is_refused_after_an_earlier_connection_succeeded()
    {
        var port = new QBittorrentPort(_handlers);
        await port.TestAsync(Connect(_server));

        var (ok, detail) = await port.TestAsync(Connect(_server, password: "wrong"));

        Assert.False(ok);
        Assert.Contains("username or password was refused", detail, StringComparison.Ordinal);
    }
}
