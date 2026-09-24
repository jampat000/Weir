using System.Net;
using System.Text;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Infrastructure.MediaManagers;

namespace Weir.Infrastructure.Tests.MediaManagers;

/// <summary>
/// The five download-client dialects (#768) against a scripted <see cref="FakeManagerHttp"/>: what each
/// <see cref="IDownloadClientPort"/> asks for, the qBittorrent and Deluge cookie logins, and Transmission's 409
/// session-id handshake.
/// </summary>
public sealed class DownloadClientPortsTests
{
    private static DownloadClientConnection Connection(
        string kind, string? username = null, string? password = null, string? apiKey = null) =>
        new(kind, "Main", "http://client.local", username, password, apiKey, 1);

    // --- SABnzbd --------------------------------------------------------------------------------

    [Fact]
    public async Task Sabnzbd_reads_the_base_and_category_folders_with_its_api_key()
    {
        var http = new FakeManagerHttp()
            .Route(HttpMethod.Get, "/api", request => request.Uri.Query.Contains("section=misc")
                ? FakeManagerHttp.Response(HttpStatusCode.OK, """{"config":{"misc":{"complete_dir":"/downloads/complete"}}}""")
                : FakeManagerHttp.Response(HttpStatusCode.OK, """{"config":{"categories":[{"name":"tv-sonarr","dir":"tv"}]}}"""));
        var port = new SabnzbdPort(http);

        var folders = await port.ReadFoldersAsync(Connection(DownloadClientKinds.Sabnzbd, apiKey: "secret-key"));

        Assert.Equal("/downloads/complete", folders.CompletedFolder);
        Assert.Equal([new DownloadClientCategoryFolder("tv-sonarr", "/downloads/complete/tv")], folders.CategoryFolders);
        Assert.All(http.Requests, request => Assert.Contains("apikey=secret-key", request.Uri.Query));
    }

    [Fact]
    public async Task Sabnzbd_test_fails_plainly_when_the_key_is_refused()
    {
        var http = new FakeManagerHttp().Json(HttpMethod.Get, "/api", "", HttpStatusCode.Forbidden);
        var port = new SabnzbdPort(http);

        var (ok, detail) = await port.TestAsync(Connection(DownloadClientKinds.Sabnzbd, apiKey: "bad-key"));

        Assert.False(ok);
        Assert.Contains("API key was refused", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sabnzbd_read_folders_is_empty_not_thrown_when_unreachable()
    {
        var http = new FakeManagerHttp().Throw(HttpMethod.Get, "/api", new HttpRequestException("refused"));
        var port = new SabnzbdPort(http);

        var folders = await port.ReadFoldersAsync(Connection(DownloadClientKinds.Sabnzbd, apiKey: "k"));

        Assert.Equal(DownloadClientFolders.Empty, folders);
    }

    // --- NZBGet -----------------------------------------------------------------------------------

    [Fact]
    public async Task Nzbget_sends_basic_auth_and_reads_config_by_json_rpc()
    {
        var http = new FakeManagerHttp().Json(
            HttpMethod.Post,
            "/jsonrpc",
            """{"version":"1.1","result":[{"Name":"DestDir","Value":"/downloads/complete"}]}""");
        var port = new NzbgetPort(http);

        var folders = await port.ReadFoldersAsync(Connection(DownloadClientKinds.Nzbget, username: "user", password: "pass"));

        Assert.Equal("/downloads/complete", folders.CompletedFolder);
        var request = Assert.Single(http.Requests);
        var expectedAuth = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass"));
        Assert.Equal(expectedAuth, request.Headers["Authorization"]);
        Assert.Equal("config", ((WireString)((WireObject)request.Json!)["method"]).Value);
    }

    [Fact]
    public async Task Nzbget_test_fails_plainly_when_credentials_are_refused()
    {
        var http = new FakeManagerHttp().Json(HttpMethod.Post, "/jsonrpc", "", HttpStatusCode.Unauthorized);
        var port = new NzbgetPort(http);

        var (ok, detail) = await port.TestAsync(Connection(DownloadClientKinds.Nzbget));

        Assert.False(ok);
        Assert.Contains("username or password was refused", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nzbget_sends_no_authorization_header_when_no_credential_is_saved()
    {
        var http = new FakeManagerHttp().Json(HttpMethod.Post, "/jsonrpc", """{"result":[]}""");
        var port = new NzbgetPort(http);

        await port.ReadFoldersAsync(Connection(DownloadClientKinds.Nzbget));

        Assert.False(Assert.Single(http.Requests).Headers.ContainsKey("Authorization"));
    }

    // --- qBittorrent ------------------------------------------------------------------------------

    [Fact]
    public async Task QBittorrent_logs_in_with_a_cookie_then_reads_categories_and_preferences()
    {
        var http = new FakeManagerHttp()
            .Route(HttpMethod.Post, "/api/v2/auth/login", _ => FakeManagerHttp.Response(HttpStatusCode.OK, "Ok.", ("Set-Cookie", "SID=abc123; HttpOnly")))
            .Json(HttpMethod.Get, "/api/v2/torrents/categories", """{"tv-sonarr":{"name":"tv-sonarr","savePath":"/downloads/complete/tv"}}""")
            .Json(HttpMethod.Get, "/api/v2/app/preferences", """{"save_path":"/downloads/complete"}""");
        var port = new QBittorrentPort(http);

        var folders = await port.ReadFoldersAsync(Connection(DownloadClientKinds.QBittorrent, username: "admin", password: "adminadmin"));

        Assert.Equal("/downloads/complete", folders.CompletedFolder);
        Assert.Equal([new DownloadClientCategoryFolder("tv-sonarr", "/downloads/complete/tv")], folders.CategoryFolders);
        var categoriesRequest = http.RequestsTo(HttpMethod.Get, "/api/v2/torrents/categories").Single();
        Assert.Equal("SID=abc123; HttpOnly", categoriesRequest.Headers["Cookie"]);
    }

    [Fact]
    public async Task QBittorrent_treats_a_200_with_Fails_body_as_a_refused_login()
    {
        var http = new FakeManagerHttp().Route(HttpMethod.Post, "/api/v2/auth/login", _ => FakeManagerHttp.Response(HttpStatusCode.OK, "Fails."));
        var port = new QBittorrentPort(http);

        var (ok, detail) = await port.TestAsync(Connection(DownloadClientKinds.QBittorrent, username: "admin", password: "wrong"));

        Assert.False(ok);
        Assert.Contains("username or password was refused", detail, StringComparison.Ordinal);
    }

    // --- Deluge -------------------------------------------------------------------------------------

    private static FakeManagerHttp DelugeHttp(string labelAnswer) =>
        new FakeManagerHttp().Route(HttpMethod.Post, "/json", request =>
        {
            var method = ((WireString)((WireObject)request.Json!)["method"]).Value;
            return method switch
            {
                "auth.login" => FakeManagerHttp.Response(HttpStatusCode.OK, """{"result":true,"error":null,"id":1}""", ("Set-Cookie", "_session_id=deluge-session")),
                "core.get_config" => FakeManagerHttp.Response(HttpStatusCode.OK, """{"result":{"move_completed_path":"/downloads/complete"},"error":null,"id":2}"""),
                "label.get_config" => FakeManagerHttp.Response(HttpStatusCode.OK, labelAnswer),
                _ => throw new InvalidOperationException($"unexpected Deluge method {method}"),
            };
        });

    [Fact]
    public async Task Deluge_logs_in_with_the_password_alone_then_reads_config_and_labels()
    {
        var http = DelugeHttp("""{"result":{"tv-sonarr":{"move_completed_path":"/downloads/complete/tv"}},"error":null,"id":3}""");
        var port = new DelugePort(http);

        var folders = await port.ReadFoldersAsync(Connection(DownloadClientKinds.Deluge, password: "deluge-password"));

        Assert.Equal("/downloads/complete", folders.CompletedFolder);
        Assert.Equal([new DownloadClientCategoryFolder("tv-sonarr", "/downloads/complete/tv")], folders.CategoryFolders);
        var loginRequest = http.Requests.First(r => ((WireString)((WireObject)r.Json!)["method"]).Value == "auth.login");
        Assert.Equal("deluge-password", ((WireString)((WireArray)((WireObject)loginRequest.Json!)["params"]).Items[0]).Value);
    }

    [Fact]
    public async Task Deluge_treats_the_label_plugin_being_disabled_as_no_labels_not_a_failure()
    {
        var http = DelugeHttp("""{"result":null,"error":{"message":"Unknown method label.get_config","code":8},"id":3}""");
        var port = new DelugePort(http);

        var folders = await port.ReadFoldersAsync(Connection(DownloadClientKinds.Deluge, password: "deluge-password"));

        Assert.Equal("/downloads/complete", folders.CompletedFolder);
        Assert.Empty(folders.CategoryFolders);
    }

    [Fact]
    public async Task Deluge_test_fails_plainly_when_the_password_is_refused()
    {
        var http = new FakeManagerHttp().Route(HttpMethod.Post, "/json", _ => FakeManagerHttp.Response(HttpStatusCode.OK, """{"result":false,"error":null,"id":1}"""));
        var port = new DelugePort(http);

        var (ok, detail) = await port.TestAsync(Connection(DownloadClientKinds.Deluge, password: "wrong"));

        Assert.False(ok);
        Assert.Contains("password was refused", detail, StringComparison.Ordinal);
    }

    // --- Transmission ---------------------------------------------------------------------------

    [Fact]
    public async Task Transmission_retries_once_with_the_session_id_the_409_named()
    {
        var calls = 0;
        var http = new FakeManagerHttp().Route(HttpMethod.Post, "/transmission/rpc", request =>
        {
            calls++;
            if (calls == 1)
            {
                return FakeManagerHttp.Response(HttpStatusCode.Conflict, null, ("X-Transmission-Session-Id", "session-abc"));
            }

            Assert.Equal("session-abc", request.Headers["X-Transmission-Session-Id"]);
            return FakeManagerHttp.Response(HttpStatusCode.OK, """{"arguments":{"download-dir":"/downloads/complete"},"result":"success"}""");
        });
        var port = new TransmissionPort(http);

        var folders = await port.ReadFoldersAsync(Connection(DownloadClientKinds.Transmission));

        Assert.Equal("/downloads/complete", folders.CompletedFolder);
        Assert.Empty(folders.CategoryFolders);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Transmission_test_succeeds_once_the_handshake_completes()
    {
        var calls = 0;
        var http = new FakeManagerHttp().Route(HttpMethod.Post, "/transmission/rpc", _ =>
        {
            calls++;
            return calls == 1
                ? FakeManagerHttp.Response(HttpStatusCode.Conflict, null, ("X-Transmission-Session-Id", "session-abc"))
                : FakeManagerHttp.Response(HttpStatusCode.OK, """{"arguments":{},"result":"success"}""");
        });
        var port = new TransmissionPort(http);

        var (ok, detail) = await port.TestAsync(Connection(DownloadClientKinds.Transmission));

        Assert.True(ok);
        Assert.Contains("Connected", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transmission_gives_up_after_one_retry_when_the_second_answer_is_still_409()
    {
        var http = new FakeManagerHttp().Route(
            HttpMethod.Post, "/transmission/rpc", _ => FakeManagerHttp.Response(HttpStatusCode.Conflict, null, ("X-Transmission-Session-Id", "session-abc")));
        var port = new TransmissionPort(http);

        var folders = await port.ReadFoldersAsync(Connection(DownloadClientKinds.Transmission));

        Assert.Equal(DownloadClientFolders.Empty, folders);
        Assert.Equal(2, http.Requests.Count);
    }
}
