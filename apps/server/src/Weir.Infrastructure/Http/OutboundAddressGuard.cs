using System.Net;
using System.Net.Sockets;
using Weir.Core.Net;

namespace Weir.Infrastructure.Http;

/// <summary>
/// Resolves a hostname and reports which resolved addresses a policy accepts, so every outbound path that needs
/// address filtering — the notification poster (public addresses only), media manager connections (LAN/private
/// allowed, link-local and metadata addresses refused) and the metadata provider (public addresses only) — shares
/// one place that does it. Stateless: nothing here is per-connection or per-request.
/// </summary>
public static class OutboundAddressGuard
{
    /// <summary>A DNS lookup, swappable in tests so a policy can be proved against fixed answers rather than real DNS.</summary>
    public delegate Task<IPAddress[]> HostResolver(string host, CancellationToken cancellationToken);

    private static Task<IPAddress[]> ResolveViaDns(string host, CancellationToken cancellationToken) => Dns.GetHostAddressesAsync(host, cancellationToken);

    /// <summary>Only a globally-routable address (the notification poster and the metadata provider: public destinations only).</summary>
    public static bool IsPublic(PyIpAddress address) => address.IsGlobal;

    /// <summary>
    /// A media manager lives on the LAN or the same host, so private ranges and loopback stay allowed — only
    /// link-local addresses (169.254.0.0/16, fe80::/10 — how cloud metadata endpoints are reached), the
    /// unspecified address and multicast are refused.
    /// </summary>
    public static bool IsLocalServiceAddress(PyIpAddress address) =>
        !address.IsLinkLocal && !address.IsUnspecified && !address.IsMulticast;

    /// <summary>An IPv4-mapped IPv6 address (<c>::ffff:a.b.c.d</c>) is classified by its embedded IPv4 form.</summary>
    public static PyIpAddress Classify(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return PyIpAddress.FromIpAddress(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
    }

    /// <summary>
    /// What resolving and filtering a host produced. <see cref="CouldNotResolve"/> distinguishes a lookup failure
    /// from a lookup that succeeded but named nothing the policy allows — a caller that reports them differently
    /// (the notification poster does) needs to tell the two apart; one that treats every refusal alike does not.
    /// </summary>
    public readonly record struct ResolvedAddresses(bool CouldNotResolve, IReadOnlyList<IPAddress> Allowed);

    /// <summary>
    /// Resolves <paramref name="host"/> (an IP literal is returned as-is, with no DNS lookup) and returns every
    /// resolved address <paramref name="isAllowed"/> accepts, in the order the resolver gave them.
    /// </summary>
    public static async Task<ResolvedAddresses> ResolveAllowedAsync(
        string host, Func<PyIpAddress, bool> isAllowed, HostResolver? resolveHost, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(isAllowed);
        IPAddress[] addresses;
        if (PyIpAddress.TryParse(host, out _) && IPAddress.TryParse(host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await (resolveHost ?? ResolveViaDns)(host, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is SocketException or ArgumentException)
            {
                return new ResolvedAddresses(CouldNotResolve: true, Allowed: []);
            }
        }

        return new ResolvedAddresses(CouldNotResolve: false, [.. addresses.Where(address => isAllowed(Classify(address)))]);
    }

    /// <summary>
    /// A <see cref="SocketsHttpHandler.ConnectCallback"/> that resolves and filters the connection's own target host
    /// — not a value captured earlier — so the address that is checked is the address that is connected to. This is
    /// what keeps the filter from being defeated by a name that resolves differently a moment after the check (DNS
    /// rebinding): the notification poster already did this for its single, known destination; this generalises it
    /// to a shared handler that serves whatever host each request names.
    /// </summary>
    public static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        Func<PyIpAddress, bool> isAllowed,
        Func<string, Exception> refused,
        HostResolver? resolveHost,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(isAllowed);
        ArgumentNullException.ThrowIfNull(refused);
        var host = context.DnsEndPoint.Host;
        var resolved = await ResolveAllowedAsync(host, isAllowed, resolveHost, cancellationToken).ConfigureAwait(false);
        if (resolved.Allowed.Count == 0)
        {
            throw refused(host);
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(resolved.Allowed[0], context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            socket.Dispose();
            throw;
        }
    }
}
