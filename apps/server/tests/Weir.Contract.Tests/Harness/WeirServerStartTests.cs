using System.Net;
using System.Net.Sockets;

namespace Weir.Contract.Tests.Harness;

/// <summary>Checks how the harness starts a server when the port it picked is taken, and when the server refuses to start for another reason.</summary>
[ContractArea("harness")]
public sealed class WeirServerStartTests
{
    private const string RefusedSetting = "WEIR_ARTWORK_GATEWAY_URL";

    [Fact]
    public async Task A_port_taken_before_the_server_binds_is_replaced_by_a_new_one()
    {
        using var occupied = Occupy();
        var picks = new Queue<int>([occupied.Port]);

        await using var server = await WeirServer.StartWithPortsAsync(null, () => picks.Count > 0 ? picks.Dequeue() : WeirServer.FreePort());

        Assert.NotEqual(occupied.Port, server.BaseUrl.Port);
        Assert.Contains($"port {occupied.Port} was taken", server.LogText());
        Assert.Contains("attempt 2 of 3", server.LogText());
        using var client = server.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/ready")).Status);
    }

    [Fact]
    public async Task A_port_that_stays_taken_fails_after_three_attempts_with_the_servers_log()
    {
        using var occupied = Occupy();
        var picks = 0;

        var failure = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            WeirServer.StartWithPortsAsync(null, () =>
            {
                picks++;
                return occupied.Port;
            }));

        Assert.Equal(WeirServer.MaxStartAttempts, picks);
        Assert.Contains("exited with code", failure.Message);
        Assert.Contains("before it was ready", failure.Message);
        Assert.Contains("address already in use", failure.Message);
    }

    [Fact]
    public async Task A_server_that_refuses_to_start_for_another_reason_fails_at_once()
    {
        var picks = 0;
        var refused = new Dictionary<string, string> { [RefusedSetting] = "not an address" };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WeirServer.StartWithPortsAsync(refused, () =>
            {
                picks++;
                return WeirServer.FreePort();
            }));

        Assert.Equal(1, picks);
        Assert.Contains("exited with code 1 before it was ready", failure.Message);
        Assert.Contains(RefusedSetting, failure.Message);
    }

    [Theory]
    [InlineData("System.IO.IOException: Failed to bind to address http://127.0.0.1:5000: address already in use.", true)]
    [InlineData("---> Microsoft.AspNetCore.Connections.AddressInUseException: Only one usage of each socket address", true)]
    [InlineData("Failed to bind to address http://127.0.0.1:80: permission denied.", false)]
    [InlineData("Weir cannot start: Invalid port 99999: expected a number from 1 to 65535.", false)]
    [InlineData("", false)]
    public void Only_an_address_in_use_failure_counts_as_a_port_clash(string log, bool expected) =>
        Assert.Equal(expected, ServerBindFailure.IsPortInUse(log));

    private static Occupant Occupy()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new Occupant(listener);
    }

    private sealed class Occupant(TcpListener listener) : IDisposable
    {
        public int Port { get; } = ((IPEndPoint)listener.LocalEndpoint).Port;

        public void Dispose() => listener.Stop();
    }
}
