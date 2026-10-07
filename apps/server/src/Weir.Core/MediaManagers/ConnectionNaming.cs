using System.Globalization;
using Weir.Core.Json;
using Weir.Core.Notifications;

namespace Weir.Core.MediaManagers;

/// <summary>One connection to be named: which product it is (Radarr, qBittorrent) and where it lives.</summary>
public readonly record struct ConnectionAddress(long Id, string Product, string BaseUrl);

/// <summary>
/// Names a media manager or download client connection after what it is and where it runs, so nobody types a name
/// that does nothing (#826): "Deluno on my-pc", "Radarr on NAS", "qBittorrent on 192.0.2.51".
/// </summary>
public static class ConnectionNaming
{
    /// <summary>
    /// The name of every connection, by id. A name is the product and the address's host as typed. Connections
    /// of one product on the same host also carry their port ("Radarr on NAS (7879)"), and their path when the
    /// port is shared too. Names never repeat: a connection with no address, or one identical to another, gets a
    /// number.
    /// </summary>
    public static IReadOnlyDictionary<long, string> NamesFor(IReadOnlyCollection<ConnectionAddress> connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        var names = new Dictionary<long, string>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var product in connections.GroupBy(connection => connection.Product, StringComparer.Ordinal))
        {
            var entries = product.OrderBy(connection => connection.Id).Select(connection => (connection.Id, Location: Locate(connection.BaseUrl))).ToList();
            var sharedHosts = entries.GroupBy(entry => BaseName(product.Key, entry.Location.Host), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, location) in entries)
            {
                var baseName = BaseName(product.Key, location.Host);
                var shareHost = sharedHosts.Contains(baseName) && location.Distinguisher.Length > 0;
                var name = shareHost ? $"{baseName} ({location.Distinguisher})" : baseName;
                names[id] = Unused(name, taken);
            }
        }

        return names;
    }

    private static string BaseName(string product, string? host) => host is null ? product : $"{product} on {host}";

    private static string Unused(string name, HashSet<string> taken)
    {
        var candidate = name;
        for (var copy = 2; !taken.Add(candidate); copy++)
        {
            candidate = string.Create(CultureInfo.InvariantCulture, $"{name} ({copy})");
        }

        return candidate;
    }

    /// <summary>The host as typed (IPv6 in brackets) and the port-and-path that tells two connections on it apart.</summary>
    private static (string? Host, string Distinguisher) Locate(string baseUrl)
    {
        SplitUrl parts;
        try
        {
            parts = SplitUrl.Parse(baseUrl.Trim());
        }
        catch (WireValueException)
        {
            return (null, string.Empty);
        }

        var host = parts.HostAsWritten;
        if (host is null)
        {
            return (null, string.Empty);
        }

        int port;
        try
        {
            port = ConnectionEndpoint.PortOf(parts);
        }
        catch (WireValueException)
        {
            return (host, string.Empty);
        }

        var path = parts.Path.Trim('/');
        var distinguisher = string.Create(CultureInfo.InvariantCulture, $"{port}");
        if (path.Length > 0)
        {
            distinguisher += "/" + path;
        }

        return (host.Contains(':', StringComparison.Ordinal) ? $"[{host}]" : host, distinguisher);
    }
}
