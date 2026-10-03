using Weir.Core.MediaManagers;
using Weir.Core.Time;

namespace Weir.Infrastructure.ConnectionTraffic;

/// <summary>Where a call to a connection has got to.</summary>
public enum ConnectionPhase
{
    /// <summary>Weir has started a call and is waiting for the answer.</summary>
    Asked,

    /// <summary>The connection replied, or called Weir.</summary>
    Answered,

    /// <summary>The connection did not reply, or replied that Weir was refused or it was broken.</summary>
    Failed,
}

/// <summary>Which side made the call.</summary>
public enum ConnectionDirection
{
    /// <summary>Weir called the connection.</summary>
    Outbound,

    /// <summary>The connection called Weir.</summary>
    Inbound,
}

/// <summary>
/// One thing that happened on a connection: the unit the live Connections view lights up on. <c>Milliseconds</c> is how long
/// an outbound call took; it is null while the call is still being asked, and for an inbound call.
/// </summary>
public sealed record ConnectionActivity(
    ConnectionRef Connection, ConnectionPhase Phase, ConnectionDirection Direction, Timestamp At, long? Milliseconds);
