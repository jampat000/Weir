using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

/// <summary>
/// The five bare download-client dialects Weir can read watched-folder suggestions from when there is no
/// Sonarr/Radarr/Deluno to ask instead (#768). A connection here is outbound only: read a client's own
/// configuration to suggest a folder, never write to it and never control it.
/// </summary>
public static class DownloadClientKinds
{
    public const string Sabnzbd = "sabnzbd";
    public const string Nzbget = "nzbget";
    public const string QBittorrent = "qbittorrent";
    public const string Deluge = "deluge";
    public const string Transmission = "transmission";

    /// <summary>Every download client kind, matching the <c>download_client_connections.kind</c> check.</summary>
    public static readonly IReadOnlyList<string> All = [Sabnzbd, Nzbget, QBittorrent, Deluge, Transmission];

    private static readonly Dictionary<string, string> KindLabels = new(StringComparer.Ordinal)
    {
        [Sabnzbd] = "SABnzbd",
        [Nzbget] = "NZBGet",
        [QBittorrent] = "qBittorrent",
        [Deluge] = "Deluge",
        [Transmission] = "Transmission",
    };

    /// <summary>A connection's label, e.g. <c>"qBittorrent (Living room)"</c>; a connection named after its product is not repeated.</summary>
    public static string LabelForConnection(string? kind, string? name)
    {
        var product = KindLabels.GetValueOrDefault(WireStrings.Strip(kind ?? string.Empty).ToLowerInvariant(), "Download client");
        var label = WireStrings.Strip(name ?? string.Empty);
        if (label.Length == 0)
        {
            return product;
        }

        return string.Equals(label, product, StringComparison.OrdinalIgnoreCase) ? label : $"{product} ({label})";
    }
}

/// <summary>
/// One configured download client, resolved far enough to talk to. Which of <see cref="Username"/>,
/// <see cref="Password"/> and <see cref="ApiKey"/> are set depends on the kind: SABnzbd uses only an API key;
/// Deluge only a password; the rest, a username and password (both optional for Transmission).
/// </summary>
public sealed record DownloadClientConnection(
    string Kind, string Name, string BaseUrl, string? Username, string? Password, string? ApiKey, long? ConnectionId = null)
{
    public string Label => DownloadClientKinds.LabelForConnection(Kind, Name);
}

/// <summary>One category or label a download client organizes completed downloads by, and the folder it saves them to.</summary>
public sealed record DownloadClientCategoryFolder(string Category, string Folder);

/// <summary>
/// What a download client reports about where it saves completed downloads (#768): the folder it uses when
/// nothing more specific applies, and — for a client that organizes by category or label — each one's own
/// folder, for the per-library "folder chain" check to compare against a library's watched folder.
/// </summary>
public sealed record DownloadClientFolders(string? CompletedFolder, IReadOnlyList<DownloadClientCategoryFolder> CategoryFolders)
{
    public static readonly DownloadClientFolders Empty = new(null, []);
}

/// <summary>A call to a download client failed with an answer Weir understood well enough to classify.</summary>
public class DownloadClientHttpException : Exception
{
    public DownloadClientHttpException()
    {
    }

    public DownloadClientHttpException(string message)
        : base(message)
    {
    }

    public DownloadClientHttpException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The download client could not be reached at all (refused, timed out, name not resolved).</summary>
public sealed class DownloadClientUnreachableException : Exception
{
    public DownloadClientUnreachableException()
    {
    }

    public DownloadClientUnreachableException(string message)
        : base(message)
    {
    }

    public DownloadClientUnreachableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>One download client dialect: test the connection, and read what it reports about its folders.</summary>
public interface IDownloadClientPort
{
    string Kind { get; }

    /// <summary>Whether Weir can reach this client with the saved credentials, and a plain-English reason either way.</summary>
    Task<(bool Ok, string Detail)> TestAsync(DownloadClientConnection connection, CancellationToken cancellationToken = default);

    /// <summary>
    /// This client's folders, or <see cref="DownloadClientFolders.Empty"/> when it could not be reached or did not
    /// answer as expected — a client that fails to answer contributes nothing to the suggestion list rather than
    /// failing it (#768).
    /// </summary>
    Task<DownloadClientFolders> ReadFoldersAsync(DownloadClientConnection connection, CancellationToken cancellationToken = default);
}

/// <summary>Plain-English phrasing shared by the five download-client dialects when a connection cannot be reached.</summary>
public static class DownloadClientDialectRules
{
    public static string Unreachable(DownloadClientConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return $"Weir could not reach {connection.Label} at {connection.BaseUrl}. " +
               "Check the address is right, and that the app is running and reachable from this machine.";
    }
}
