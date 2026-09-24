using System.Net;
using System.Numerics;
using Weir.Core.Json;
using Weir.Infrastructure.MediaManagers;

namespace Weir.Infrastructure.Tests.MediaManagers;

/// <summary>
/// Download client connections (#768): encrypted secrets round-trip and are never returned in plain text, and
/// <see cref="DownloadClientSuggestions"/> produces the exact shape the library editor's suggestion list expects,
/// silently omitting a connection whose client did not answer.
/// </summary>
public sealed class DownloadClientConnectionServiceTests
{
    [Fact]
    public async Task A_saved_api_key_is_never_returned_but_decrypts_for_an_outbound_call()
    {
        using var fixture = new DownloadClientFixture();
        var id = await fixture.AddConnectionAsync("sabnzbd", "My SABnzbd", apiKey: "plain-api-key");

        var row = await fixture.Db(uow => DownloadClientConnectionStore.GetAsync(uow, id));
        Assert.NotNull(row);
        Assert.NotEqual("plain-api-key", row!.ApiKeyCiphertext);
        var output = row.ToOut();
        Assert.True(((PyBool)output["api_key_is_saved"]).Value);
        Assert.False(output.ContainsKey("api_key"));

        var resolved = fixture.Connections.ConnectionFromRow(row);
        Assert.Equal("plain-api-key", resolved!.ApiKey);
    }

    [Fact]
    public async Task A_password_only_deluge_connection_needs_no_username_to_resolve()
    {
        using var fixture = new DownloadClientFixture();
        var id = await fixture.AddConnectionAsync("deluge", "Deluge", password: "deluge-secret");

        var row = await fixture.Db(uow => DownloadClientConnectionStore.GetAsync(uow, id));
        var resolved = fixture.Connections.ConnectionFromRow(row!);

        Assert.Null(resolved!.Username);
        Assert.Equal("deluge-secret", resolved.Password);
    }

    [Fact]
    public async Task An_open_transmission_connection_with_no_credential_still_resolves()
    {
        using var fixture = new DownloadClientFixture();
        var id = await fixture.AddConnectionAsync("transmission", "Transmission");

        var row = await fixture.Db(uow => DownloadClientConnectionStore.GetAsync(uow, id));
        var resolved = fixture.Connections.ConnectionFromRow(row!);

        Assert.NotNull(resolved);
        Assert.Null(resolved!.Username);
        Assert.Null(resolved.Password);
    }

    [Fact]
    public async Task Saving_a_secret_without_a_configured_credentials_secret_names_the_env_var()
    {
        using var fixture = new DownloadClientFixture();
        var noSecretCipher = new Core.Security.CredentialCipher(null, null, [], TimeProvider.System);
        var service = new DownloadClientConnectionService(noSecretCipher);

        var thrown = await Assert.ThrowsAsync<DownloadClientConnectionException>(
            () => fixture.Db(uow => service.CreateAsync(uow, "sabnzbd", "No Secret", "http://192.0.2.20:8080", apiKey: "some-key")));

        Assert.Contains("WEIR_CREDENTIALS_SECRET", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Suggestions_carry_the_shape_the_library_editor_expects()
    {
        using var fixture = new DownloadClientFixture();
        fixture.Http
            .Route(HttpMethod.Post, "/api/v2/auth/login", _ => FakeManagerHttp.Response(HttpStatusCode.OK, "Ok.", ("Set-Cookie", "SID=abc")))
            .Json(HttpMethod.Get, "/api/v2/torrents/categories", """{"tv-sonarr":{"name":"tv-sonarr","savePath":"/downloads/complete/tv"}}""")
            .Json(HttpMethod.Get, "/api/v2/app/preferences", """{"save_path":"/downloads/complete"}""");
        var id = await fixture.AddConnectionAsync("qbittorrent", "My qBittorrent", username: "admin", password: "adminadmin");

        var suggestions = await fixture.Db(uow => fixture.Suggestions.SuggestAsync(uow), commit: false);

        var entry = Assert.Single(suggestions);
        Assert.Equal((BigInteger)id, ((PyInt)entry["connection_id"]).Value);
        Assert.Equal("qbittorrent", ((PyStr)entry["kind"]).Value);
        Assert.Equal("My qBittorrent", ((PyStr)entry["name"]).Value);
        Assert.Equal("qBittorrent (My qBittorrent)", ((PyStr)entry["label"]).Value);
        Assert.Equal("download_client", ((PyStr)entry["flow"]).Value);
        Assert.True(((PyBool)entry["ready"]).Value);
        Assert.Equal("/downloads/complete", ((PyStr)entry["suggested_watched_folder"]).Value);
        var categoryFolders = (PyList)entry["category_folders"];
        var category = (PyDict)categoryFolders.Items[0];
        Assert.Equal("tv-sonarr", ((PyStr)category["category"]).Value);
        Assert.Equal("/downloads/complete/tv", ((PyStr)category["folder"]).Value);
        var lines = (PyList)entry["lines"];
        Assert.Contains(lines.Items, line => ((PyStr)((PyDict)line)["state"]).Value == "ok");
    }

    [Fact]
    public async Task A_connection_whose_client_does_not_answer_is_omitted_not_erroring_the_whole_list()
    {
        using var fixture = new DownloadClientFixture();
        fixture.Http.Throw(HttpMethod.Get, "/api", new HttpRequestException("refused"));
        await fixture.AddConnectionAsync("sabnzbd", "Unreachable SABnzbd", apiKey: "key");

        var suggestions = await fixture.Db(uow => fixture.Suggestions.SuggestAsync(uow), commit: false);

        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task A_disabled_connection_is_not_asked_at_all()
    {
        using var fixture = new DownloadClientFixture();
        fixture.Http.Json(HttpMethod.Post, "/transmission/rpc", """{"arguments":{"download-dir":"/downloads/complete"},"result":"success"}""");
        await fixture.AddConnectionAsync("transmission", "Off", enabled: false);

        var suggestions = await fixture.Db(uow => fixture.Suggestions.SuggestAsync(uow), commit: false);

        Assert.Empty(suggestions);
        Assert.Empty(fixture.Http.Requests);
    }
}
