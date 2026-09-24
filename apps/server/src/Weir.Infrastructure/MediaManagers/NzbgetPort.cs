using System.Text;
using Weir.Core.Json;
using Weir.Core.MediaManagers;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>
/// NZBGet, read only (#768): a JSON-RPC <c>config</c> call to <c>POST {base_url}/jsonrpc</c>, authenticated with
/// HTTP basic auth from the saved username/password when either is set.
/// </summary>
public sealed class NzbgetPort : IDownloadClientPort
{
    private readonly IManagerHttpHandlerFactory _handlers;

    public NzbgetPort(IManagerHttpHandlerFactory handlers)
    {
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
    }

    public string Kind => DownloadClientKinds.Nzbget;

    public async Task<(bool Ok, string Detail)> TestAsync(DownloadClientConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        try
        {
            var response = await CallConfigAsync(connection, cancellationToken).ConfigureAwait(false);
            if (response.Status is 401 or 403)
            {
                return (false, $"Weir reached {connection.Label}, but the username or password was refused. Check them and save again.");
            }

            return response.Status is >= 200 and < 300 && response.Json() is PyDict { } dict && dict.Get("result") is PyList
                ? (true, $"Connected. Weir can reach {connection.Label}.")
                : (false, $"Weir reached {connection.Label} but did not get the answer it expected. Check the address points at NZBGet itself.");
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
            var response = await CallConfigAsync(connection, cancellationToken).ConfigureAwait(false);
            return response.Status is >= 200 and < 300 ? NzbgetRules.ParseConfig(response.Json()) : DownloadClientFolders.Empty;
        }
        catch (DownloadClientUnreachableException)
        {
            return DownloadClientFolders.Empty;
        }
    }

    private Task<DownloadClientHttpResponse> CallConfigAsync(DownloadClientConnection connection, CancellationToken cancellationToken)
    {
        var client = new DownloadClientHttpClient(connection.BaseUrl, _handlers);
        var body = new PyDict().Set("method", "config").Set("params", new PyList());
        return client.SendAsync(
            HttpMethod.Post, "/jsonrpc", content: DownloadClientHttpClient.JsonContent(body), headers: BasicAuthHeader(connection), cancellationToken: cancellationToken);
    }

    private static List<KeyValuePair<string, string>>? BasicAuthHeader(DownloadClientConnection connection)
    {
        if (string.IsNullOrEmpty(connection.Username) && string.IsNullOrEmpty(connection.Password))
        {
            return null;
        }

        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{connection.Username}:{connection.Password}"));
        return [new("Authorization", $"Basic {token}")];
    }
}
