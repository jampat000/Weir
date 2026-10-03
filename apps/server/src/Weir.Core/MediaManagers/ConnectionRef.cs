namespace Weir.Core.MediaManagers;

/// <summary>The two kinds of connection Weir keeps: a media manager it works with, and a download client it reads from.</summary>
public enum ConnectionKind
{
    MediaManager,
    DownloadClient,
}

/// <summary>Which saved connection a call to, or from, an outside app belongs to.</summary>
public readonly record struct ConnectionRef(ConnectionKind Kind, long Id)
{
    /// <summary>The media manager connection with this id, or null for the environment credentials, which are not a saved connection.</summary>
    public static ConnectionRef? ForManager(long? connectionId) =>
        connectionId is { } id ? new ConnectionRef(ConnectionKind.MediaManager, id) : null;

    /// <summary>The download client connection with this id, or null when the connection is not saved.</summary>
    public static ConnectionRef? ForDownloadClient(long? connectionId) =>
        connectionId is { } id ? new ConnectionRef(ConnectionKind.DownloadClient, id) : null;
}
