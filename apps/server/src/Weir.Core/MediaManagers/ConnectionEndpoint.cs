using Weir.Core.Json;
using Weir.Core.Notifications;

namespace Weir.Core.MediaManagers;

/// <summary>Where a connection's address points: the host (lower-cased) and the port, the scheme's own when the address has none.</summary>
public readonly record struct ConnectionEndpoint(string Host, int Port)
{
    private const int DefaultHttpPort = 80;
    private const int DefaultHttpsPort = 443;

    /// <summary>The address's endpoint, or null when it has no usable host or port.</summary>
    public static ConnectionEndpoint? Parse(string? baseUrl)
    {
        try
        {
            var parts = SplitUrl.Parse((baseUrl ?? string.Empty).Trim());
            return parts.Hostname is { } host ? new ConnectionEndpoint(host, PortOf(parts)) : null;
        }
        catch (WireValueException)
        {
            return null;
        }
    }

    /// <summary>The port written in the address, otherwise the one its scheme implies. Throws <see cref="WireValueException"/> for a port that is not one.</summary>
    internal static int PortOf(SplitUrl address) => address.Port ?? (address.Scheme == "https" ? DefaultHttpsPort : DefaultHttpPort);
}
