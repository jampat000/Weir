using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Weir.Core.Configuration;
using Weir.Host;

namespace Weir.Api.Tests;

/// <summary>
/// <see cref="HealthCheckCommand"/> is what the Docker image's HEALTHCHECK runs instead of curl
/// (the repo root Dockerfile). These exercise it against a real loopback listener, not the
/// in-memory <see cref="WeirTestServer"/>, because the command makes a real HTTP connection.
/// </summary>
public sealed class HealthCheckCommandTests
{
    [Fact]
    public async Task A_server_answering_health_with_200_exits_zero()
    {
        var port = ReserveFreeTcpPort();
        using var listener = StartOneShotHealthListener(port, StatusCodes.Status200OK);

        var exitCode = await HealthCheckCommand.RunAsync(
            ["--port", port.ToString(CultureInfo.InvariantCulture)],
            EmptyRuntime(),
            TextWriter.Null);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task A_server_answering_health_with_503_exits_nonzero()
    {
        var port = ReserveFreeTcpPort();
        using var listener = StartOneShotHealthListener(port, StatusCodes.Status503ServiceUnavailable);

        var exitCode = await HealthCheckCommand.RunAsync(
            ["--port", port.ToString(CultureInfo.InvariantCulture)],
            EmptyRuntime(),
            TextWriter.Null);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Nothing_listening_on_the_port_exits_nonzero()
    {
        var port = ReserveFreeTcpPort();

        var exitCode = await HealthCheckCommand.RunAsync(
            ["--port", port.ToString(CultureInfo.InvariantCulture)],
            EmptyRuntime(),
            TextWriter.Null);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task An_invalid_port_exits_nonzero_without_connecting()
    {
        var exitCode = await HealthCheckCommand.RunAsync(
            ["--port", "not-a-port"],
            EmptyRuntime(),
            TextWriter.Null);

        Assert.Equal(1, exitCode);
    }

    private static RuntimeEnvironment EmptyRuntime() => new(
        new Dictionary<string, string>(StringComparer.Ordinal),
        OperatingSystem.IsWindows(),
        Path.GetTempPath(),
        Path.GetTempPath());

    /// <summary>Binds port 0 to let the OS assign a free one, then releases it for the test to use.</summary>
    private static int ReserveFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Answers exactly one request on <paramref name="port"/> with <paramref name="statusCode"/>.</summary>
    private static OneShotHealthListener StartOneShotHealthListener(int port, int statusCode) =>
        new(port, statusCode);

    private sealed class OneShotHealthListener : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly Task _respond;

        public OneShotHealthListener(int port, int statusCode)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _respond = RespondOnceAsync(statusCode);
        }

        public void Dispose()
        {
            _listener.Close();
            try
            {
                _respond.GetAwaiter().GetResult();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
            {
                // Expected when a test (Nothing_listening_on_the_port_exits_nonzero doesn't use this
                // helper at all, but a 200/503 test disposing before the one request arrives would)
                // closes the listener while GetContextAsync is still waiting.
            }
        }

        private async Task RespondOnceAsync(int statusCode)
        {
            var context = await _listener.GetContextAsync().ConfigureAwait(false);
            context.Response.StatusCode = statusCode;
            context.Response.Close();
        }
    }
}
