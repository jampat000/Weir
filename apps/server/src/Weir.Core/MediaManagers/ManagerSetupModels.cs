using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

/// <summary>One of the manager's remote path mappings (<c>GET /api/v3/remotepathmapping</c>).</summary>
public sealed record RemotePathMappingEntry(string Host, string RemotePath, string LocalPath);

/// <summary>One download client the manager uses (<c>GET /api/v3/downloadclient</c>), reduced to what the check reads.</summary>
public sealed record ArrDownloadClientEntry(
    string Name, string Implementation, bool Enabled, string? Host, string? Category, string? Directory, string? Protocol = null)
{
    /// <summary><c>protocol</c> is the camelCased <c>DownloadProtocol</c>: <c>torrent</c> or <c>usenet</c>.</summary>
    public bool IsTorrent => string.Equals(Protocol, "torrent", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One line of a setup check: fine, a problem with its fix, or a note worth reading.</summary>
public sealed record SetupCheckLine(string State, string Text)
{
    public const string Ok = "ok";
    public const string Problem = "problem";
    public const string Note = "note";

    public PyDict ToOut() => new PyDict().Set("state", State).Set("text", Text);
}

/// <summary>What a Sonarr/Radarr check found: the hosts to map, and the lines to show.</summary>
public sealed record ArrSetupResult(IReadOnlyList<string> Hosts, IReadOnlyList<SetupCheckLine> Lines);

/// <summary>What a Deluno check found: the folders it reports for this media type, and the lines to show.</summary>
public sealed record DelunoSetupResult(string? WatchedFolder, string? OutputFolder, IReadOnlyList<SetupCheckLine> Lines);
