using System.Net;
using System.Net.Sockets;
using Weir.Tray.LanAccess;

using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// The tray's server host with real stand-in servers that answer, or do not, on a port of their own: how long it waits for a
/// server that has said nothing and for one that has said it is starting, that a server that never became ready is stopped, and
/// that the icon says "Stopping..." while a server finishes. The waits are shortened to a second or so; no test asserts how long
/// anything took.
/// </summary>
public sealed class ServerHostTests : IDisposable
{
    private const string ReadyAfterVariable = "WEIR_STANDIN_READY_AFTER_MS";
    private const string StopAfterVariable = "WEIR_STANDIN_STOP_AFTER_MS";
    private const int TestTimeoutMs = 60_000;

    private readonly TempDirectory _home = TempDirectory.AsWeirHome();
    private readonly List<ServerHost> _hosts = [];

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            host.StopAsync().GetAwaiter().GetResult();
            host.Dispose();
        }

        Environment.SetEnvironmentVariable(ReadyAfterVariable, null);
        Environment.SetEnvironmentVariable(StopAfterVariable, null);
        _home.Dispose();
    }

    private ServerHost Host(TimeSpan nothingAnswers, TimeSpan starting, string? readyAfterMs = null, string? stopAfterMs = null)
    {
        Environment.SetEnvironmentVariable(ReadyAfterVariable, readyAfterMs);
        Environment.SetEnvironmentVariable(StopAfterVariable, stopAfterMs);
        var install = Path.Combine(_home.Path, "install-" + Guid.NewGuid().ToString("n"));
        StandInServers.Install(Path.Combine(install, "server"));
        var host = new ServerHost(_home.Path, install, FreePort(), ListenScope.ThisPcOnly, nothingAnswers, starting);
        _hosts.Add(host);
        return host;
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task A_server_that_does_not_listen_fails_at_the_short_limit_and_is_stopped_rather_than_left_running()
    {
        // The long limit is two minutes: waiting for it would run the test out of time.
        var host = Host(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(2), readyAfterMs: "-1");

        await Assert.ThrowsAsync<TimeoutException>(() => host.StartAsync(CancellationToken.None));

        Assert.False(host.ServerIsRunning);
        Assert.Equal(ServerPhase.Stopped, host.Phase);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task A_server_that_answers_not_ready_is_waited_for_past_the_short_limit_until_it_is_ready()
    {
        // Ready seven seconds after it starts listening, which is after the six-second short limit however soon it started.
        var host = Host(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(60), readyAfterMs: "7000");

        await host.StartAsync(CancellationToken.None);

        Assert.True(host.ServerIsRunning);
        Assert.Equal(ServerPhase.Running, host.Phase);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task The_icon_says_stopping_while_the_server_finishes_and_stopped_after()
    {
        var host = Host(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), stopAfterMs: "1000");
        await host.StartAsync(CancellationToken.None);
        var phases = new List<ServerPhase>();
        host.PhaseChanged += () => phases.Add(host.Phase);

        await host.StopAsync();

        Assert.Equal([ServerPhase.Stopping, ServerPhase.Stopped], phases);
        Assert.False(host.ServerIsRunning);
    }
}
