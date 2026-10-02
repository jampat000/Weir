using Weir.Core.MediaManagers;

namespace Weir.Infrastructure.ConnectionTraffic;

/// <summary>
/// Marks an outgoing request as a call to one saved connection, so <see cref="ConnectionActivityHandler"/> reports it.
/// A request with no mark (the metadata service, a connection that is not saved) reports nothing.
/// </summary>
public static class ConnectionTag
{
    private static readonly HttpRequestOptionsKey<ConnectionRef> Key = new("Weir.Connection");

    public static void Apply(HttpRequestMessage request, ConnectionRef? connection)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (connection is { } marked)
        {
            request.Options.Set(Key, marked);
        }
    }

    internal static bool TryRead(HttpRequestMessage request, out ConnectionRef connection) => request.Options.TryGetValue(Key, out connection);
}
