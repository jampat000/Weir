using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Weir.Infrastructure.Http;
using Weir.Infrastructure.MediaManagers;
using Xunit.Abstractions;

namespace Weir.Infrastructure.Tests.Http;

/// <summary>
/// A program that listens on IPv4 only must be reachable as <c>localhost</c> without Windows first retrying a refused
/// connect to ::1 for about two seconds. The listener here is bound to 127.0.0.1 alone.
/// </summary>
public sealed class LoopbackFirstConnectTests(ITestOutputHelper output) : IDisposable
{
    private static readonly byte[] OkResponse = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
    private static readonly TimeSpan Ceiling = TimeSpan.FromMilliseconds(1000);

    private readonly TcpListener _listener = StartListener();
    private readonly CancellationTokenSource _stop = new();

    private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }

    [Fact]
    public async Task The_manager_handler_reaches_an_ipv4_only_listener_as_localhost_quickly()
    {
        using var factory = new SocketsManagerHttpHandlerFactory();

        await AssertReachedQuicklyAsync(factory.Handler(followRedirects: false, ManagerAddressPolicy.Local));
    }

    [Fact]
    public async Task The_redirect_following_manager_handler_reaches_an_ipv4_only_listener_as_localhost_quickly()
    {
        using var factory = new SocketsManagerHttpHandlerFactory();

        await AssertReachedQuicklyAsync(factory.Handler(followRedirects: true, ManagerAddressPolicy.Local));
    }

    [Fact]
    public async Task A_plain_handler_using_the_shared_callback_reaches_an_ipv4_only_listener_as_localhost_quickly()
    {
        using var handler = new SocketsHttpHandler { ConnectCallback = LoopbackFirstConnect.ConnectAsync };

        await AssertReachedQuicklyAsync(handler);
    }

    [Fact]
    public async Task The_host_name_is_matched_without_regard_to_case()
    {
        using var handler = new SocketsHttpHandler { ConnectCallback = LoopbackFirstConnect.ConnectAsync };
        using var client = new HttpClient(handler, disposeHandler: false);
        _ = ServeAsync();

        using var response = await client.GetAsync(new Uri($"http://LocalHost:{Port}/"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_address_literal_connects_as_the_handler_would_by_default()
    {
        using var handler = new SocketsHttpHandler { ConnectCallback = LoopbackFirstConnect.ConnectAsync };
        using var client = new HttpClient(handler, disposeHandler: false);
        _ = ServeAsync();

        using var response = await client.GetAsync(new Uri($"http://127.0.0.1:{Port}/"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_refused_candidate_falls_through_to_the_next()
    {
        _ = ServeAsync();
        IPAddress[] candidates = Socket.OSSupportsIPv6 ? [IPAddress.IPv6Loopback, IPAddress.Loopback] : [IPAddress.Loopback];

        await using var stream = await LoopbackFirstConnect.ConnectAsync(candidates, Port, CancellationToken.None);

        Assert.True(stream.CanWrite);
    }

    [Fact]
    public async Task When_no_candidate_answers_the_failure_is_thrown()
    {
        await Assert.ThrowsAsync<SocketException>(async () => await LoopbackFirstConnect.ConnectAsync([IPAddress.Loopback], FreePort(), CancellationToken.None));
    }

    [Fact]
    public async Task A_cancelled_connect_throws_cancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await LoopbackFirstConnect.ConnectAsync([IPAddress.Loopback], Port, cancelled.Token));
    }

    [Fact]
    public void Localhost_is_tried_on_ipv4_before_ipv6()
    {
        var addresses = LoopbackFirstConnect.LoopbackAddresses();

        Assert.Equal(IPAddress.Loopback, addresses[0]);
        Assert.All(addresses.Skip(1), address => Assert.Equal(IPAddress.IPv6Loopback, address));
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST", true)]
    [InlineData("localhost.", true)]
    [InlineData("notlocalhost", false)]
    [InlineData("localhost.example.com", false)]
    [InlineData("127.0.0.1", false)]
    public void Only_the_name_localhost_is_special(string host, bool expected) =>
        Assert.Equal(expected, LoopbackFirstConnect.IsLocalhost(host));

    private async Task AssertReachedQuicklyAsync(HttpMessageHandler handler)
    {
        using var client = new HttpClient(handler, disposeHandler: false);
        _ = ServeAsync();

        var timer = Stopwatch.StartNew();
        using var response = await client.GetAsync(new Uri($"http://localhost:{Port}/"));
        timer.Stop();

        output.WriteLine($"GET http://localhost:{Port}/ answered {(int)response.StatusCode} in {timer.ElapsedMilliseconds} ms");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(timer.Elapsed < Ceiling, $"Took {timer.ElapsedMilliseconds} ms to reach a listener on 127.0.0.1 as localhost.");
    }

    private async Task ServeAsync()
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            await using (stream.ConfigureAwait(false))
            {
                _ = await stream.ReadAsync(new byte[1024], _stop.Token).ConfigureAwait(false);
                await stream.WriteAsync(OkResponse, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException or OperationCanceledException or IOException)
        {
        }
    }

    private static TcpListener StartListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return listener;
    }

    private static int FreePort()
    {
        var probe = StartListener();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
