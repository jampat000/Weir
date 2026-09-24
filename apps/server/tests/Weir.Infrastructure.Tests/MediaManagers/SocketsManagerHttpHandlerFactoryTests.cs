using System.Net;
using System.Net.Sockets;
using System.Text;
using Weir.Core.MediaManagers;
using Weir.Infrastructure.MediaManagers;

namespace Weir.Infrastructure.Tests.MediaManagers;

/// <summary>
/// The production handler factory end to end: a fake resolver stands in for DNS, and a raw loopback listener
/// proves an allowed address is actually reached, not merely classified as allowed.
/// </summary>
public sealed class SocketsManagerHttpHandlerFactoryTests : IDisposable
{
    private static readonly byte[] OkResponse = Encoding.ASCII.GetBytes(
        "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 15\r\nConnection: close\r\n\r\n{\"status\":\"ok\"}");

    private readonly TcpListener _listener;

    public SocketsManagerHttpHandlerFactoryTests()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptOnceAsync();
    }

    private int Port { get; }

    public void Dispose() => _listener.Stop();

    private async Task AcceptOnceAsync()
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            using var stream = client.GetStream();
            var buffer = new byte[1024];
            _ = await stream.ReadAsync(buffer).ConfigureAwait(false);
            await stream.WriteAsync(OkResponse).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
        }
    }

    [Fact]
    public async Task A_manager_hostname_that_resolves_to_a_metadata_address_is_refused_before_any_connection_is_made()
    {
        using var factory = new SocketsManagerHttpHandlerFactory((_, _) => Task.FromResult(new[] { IPAddress.Parse("169.254.169.254") }));
        var client = new MediaManagerHttpClient("http://manager.invalid:1234", "k", factory);

        var error = await Assert.ThrowsAsync<MediaManagerUnreachableException>(() => client.GetJsonAsync("/health"));

        Assert.DoesNotContain("169.254", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_manager_hostname_that_resolves_to_loopback_is_reached()
    {
        using var factory = new SocketsManagerHttpHandlerFactory((_, _) => Task.FromResult(new[] { IPAddress.Loopback }));
        var client = new MediaManagerHttpClient($"http://manager.invalid:{Port}", "k", factory);

        var answer = await client.GetJsonAsync("/health");

        Assert.NotNull(answer);
    }
}
