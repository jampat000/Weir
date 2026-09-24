using Weir.Core.MediaManagers;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>The dialect for a download client kind, or null for a kind Weir does not know. Mirrors <see cref="IMediaManagerPorts"/>.</summary>
public interface IDownloadClientPorts
{
    IDownloadClientPort? PortForKind(string? kind);
}

/// <summary>Every <see cref="IDownloadClientPort"/> the container knows, indexed by kind.</summary>
public sealed class DownloadClientPorts : IDownloadClientPorts
{
    private readonly Dictionary<string, IDownloadClientPort> _byKind;

    public DownloadClientPorts(IEnumerable<IDownloadClientPort> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        _byKind = ports.ToDictionary(port => port.Kind, StringComparer.Ordinal);
    }

    public IDownloadClientPort? PortForKind(string? kind) =>
        kind is not null && _byKind.TryGetValue(kind, out var port) ? port : null;
}
