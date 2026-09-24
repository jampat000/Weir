using Weir.Core.Json;
using Weir.Core.MediaManagers;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>
/// Deluge, read only (#768): its Web UI JSON-RPC at <c>{base_url}/json</c>. <c>auth.login</c> with the saved
/// password (Deluge's Web UI has no username) establishes a session cookie, then <c>core.get_config</c> for the
/// base completed folder and <c>label.get_config</c> for each label's own — the label plugin not being enabled
/// answers a JSON-RPC error, read as "no per-label folders" rather than a failed connection.
/// </summary>
public sealed class DelugePort : IDownloadClientPort
{
    private readonly IManagerHttpHandlerFactory _handlers;

    public DelugePort(IManagerHttpHandlerFactory handlers)
    {
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
    }

    public string Kind => DownloadClientKinds.Deluge;

    public async Task<(bool Ok, string Detail)> TestAsync(DownloadClientConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        try
        {
            var client = new DownloadClientHttpClient(connection.BaseUrl, _handlers);
            var (ok, _) = await LoginAsync(client, connection, cancellationToken).ConfigureAwait(false);
            return ok
                ? (true, $"Connected. Weir can reach {connection.Label}.")
                : (false, $"Weir reached {connection.Label}, but the password was refused. Check it and save it again.");
        }
        catch (DownloadClientUnreachableException)
        {
            return (false, DownloadClientDialectRules.Unreachable(connection));
        }
    }

    public async Task<DownloadClientFolders> ReadFoldersAsync(DownloadClientConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        try
        {
            var client = new DownloadClientHttpClient(connection.BaseUrl, _handlers);
            var (ok, cookie) = await LoginAsync(client, connection, cancellationToken).ConfigureAwait(false);
            if (!ok)
            {
                return DownloadClientFolders.Empty;
            }

            var headers = CookieHeader(cookie);
            var core = await CallAsync(client, "core.get_config", headers, 2, cancellationToken).ConfigureAwait(false);
            var labels = await CallAsync(client, "label.get_config", headers, 3, cancellationToken).ConfigureAwait(false);
            return DelugeRules.Parse(core.Json(), labels.Json());
        }
        catch (DownloadClientUnreachableException)
        {
            return DownloadClientFolders.Empty;
        }
    }

    private static async Task<(bool Ok, string? Cookie)> LoginAsync(DownloadClientHttpClient client, DownloadClientConnection connection, CancellationToken cancellationToken)
    {
        var response = await CallAsync(client, "auth.login", null, 1, cancellationToken, new PyList([PyJson.Of(connection.Password ?? string.Empty)])).ConfigureAwait(false);
        var ok = response.Status is >= 200 and < 300 && response.Json() is PyDict dict && dict.Get("result") is PyBool { Value: true };
        return (ok, ok ? response.Headers.GetValueOrDefault("Set-Cookie") : null);
    }

    private static Task<DownloadClientHttpResponse> CallAsync(
        DownloadClientHttpClient client, string method, IReadOnlyList<KeyValuePair<string, string>>? headers, int id, CancellationToken cancellationToken, PyList? parameters = null)
    {
        var body = new PyDict().Set("method", method).Set("params", parameters ?? new PyList()).Set("id", id);
        return client.SendAsync(HttpMethod.Post, "/json", content: DownloadClientHttpClient.JsonContent(body), headers: headers, cancellationToken: cancellationToken);
    }

    private static List<KeyValuePair<string, string>>? CookieHeader(string? cookie) =>
        string.IsNullOrEmpty(cookie) ? null : [new("Cookie", cookie)];
}
