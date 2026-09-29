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

/// <summary>
/// One line of a setup check: fine, a problem with its fix, a note worth reading, or something Weir could not verify.
/// <see cref="Ok"/> is only ever used for a fact Weir has read for itself; a claim it can only take on someone's word is
/// <see cref="Unverified"/>, which never counts as a problem and never as a pass.
/// </summary>
public sealed record SetupCheckLine(string State, string Text)
{
    public const string Ok = "ok";
    public const string Problem = "problem";
    public const string Note = "note";
    public const string Unverified = "unverified";

    public WireObject ToOut() => new WireObject().Set("state", State).Set("text", Text);
}

/// <summary>What a Sonarr/Radarr check found: the hosts to map, and the lines to show.</summary>
public sealed record ArrSetupResult(IReadOnlyList<string> Hosts, IReadOnlyList<SetupCheckLine> Lines);

/// <summary>
/// One download client Deluno says it uses (its manifest's <c>downloadClients</c>). The manifest names the category each media
/// type is filed under but publishes no save folder, so a folder is never part of this.
/// </summary>
public sealed record ManagerDownloadClientDescriptor(string Name, bool Enabled, string? MoviesCategory, string? TvCategory);

/// <summary>What a Deluno check found: the folders it reports for this media type, and the lines to show.</summary>
public sealed record DelunoSetupResult(string? WatchedFolder, string? OutputFolder, IReadOnlyList<SetupCheckLine> Lines);
