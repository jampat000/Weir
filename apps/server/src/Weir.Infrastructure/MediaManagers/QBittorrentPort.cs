using Weir.Core.MediaManagers;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>
/// qBittorrent, read only: a cookie login (<c>POST /api/v2/auth/login</c>, body <c>Ok.</c>/<c>Fails.</c>,
/// session in <c>Set-Cookie</c>) followed by its categories and preferences. The cookie is captured per call
/// rather than kept in a jar across requests — each read is otherwise independent, so there is nothing to share.
/// </summary>
public sealed class QBittorrentPort : IDownloadClientPort
{
    private readonly IManagerHttpHandlerFactory _handlers;

    public QBittorrentPort(IManagerHttpHandlerFactory handlers)
    {
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
    }

    public string Kind => DownloadClientKinds.QBittorrent;

    public async Task<(bool Ok, string Detail)> TestAsync(DownloadClientConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        try
        {
            var client = new DownloadClientHttpClient(connection.BaseUrl, _handlers);
            var (ok, cookie) = await LoginAsync(client, connection, cancellationToken).ConfigureAwait(false);
            if (!ok)
            {
                return (false, $"Weir reached {connection.Label}, but the username or password was refused. Check them and save again.");
            }

            var preferences = await client.SendAsync(
                HttpMethod.Get, "/api/v2/app/preferences", headers: CookieHeader(cookie), cancellationToken: cancellationToken).ConfigureAwait(false);
            return preferences.Status is >= 200 and < 300
                ? (true, $"Connected. Weir can reach {connection.Label}.")
                : (false, $"Weir reached {connection.Label} but did not get the answer it expected. Check the address points at qBittorrent itself.");
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
            var categories = await client.SendAsync(HttpMethod.Get, "/api/v2/torrents/categories", headers: headers, cancellationToken: cancellationToken).ConfigureAwait(false);
            var preferences = await client.SendAsync(HttpMethod.Get, "/api/v2/app/preferences", headers: headers, cancellationToken: cancellationToken).ConfigureAwait(false);
            return QBittorrentRules.Parse(categories.Json(), preferences.Json());
        }
        catch (DownloadClientUnreachableException)
        {
            return DownloadClientFolders.Empty;
        }
    }

    /// <summary>qBittorrent answers 200 whether the login succeeded or not, so the real answer is the body text, not the status.</summary>
    private static async Task<(bool Ok, string? Cookie)> LoginAsync(DownloadClientHttpClient client, DownloadClientConnection connection, CancellationToken cancellationToken)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = connection.Username ?? string.Empty,
            ["password"] = connection.Password ?? string.Empty,
        });
        var response = await client.SendAsync(HttpMethod.Post, "/api/v2/auth/login", content: form, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (response.Status is < 200 or >= 300 || !string.Equals(response.BodyText.Trim(), "Ok.", StringComparison.Ordinal))
        {
            return (false, null);
        }

        return (true, response.Headers.GetValueOrDefault("Set-Cookie"));
    }

    private static List<KeyValuePair<string, string>>? CookieHeader(string? cookie) =>
        string.IsNullOrEmpty(cookie) ? null : [new("Cookie", cookie)];
}
