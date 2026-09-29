using System.Diagnostics;

using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// Quitting, and restarting to update, stop the server cleanly before Velopack is handed anything: Velopack's
/// installer kills whatever of Weir it finds running, which is not a stop the server can finish its jobs in (#857).
/// The server is the real stand-in process; what its exit code and state look like when the install starts is what
/// is asserted.
/// </summary>
public sealed class TrayShutdownTests : IDisposable
{
    private readonly TempDirectory _home = TempDirectory.AsWeirHome();
    private readonly StandInServers _servers = new();

    public void Dispose()
    {
        _servers.Dispose();
        _home.Dispose();
    }

    [Fact]
    public async Task Quitting_stops_the_server_cleanly_before_the_update_is_installed()
    {
        var server = await _servers.StartAsync(ServerFolder());
        var update = new FakeUpdateService().AlreadyDownloaded();
        var atInstall = ObserveServerWhenInstallStarts(update, server);

        await Shutdown(server, update).QuitAsync();

        Assert.Equal([FakeUpdateService.AppliedAndExited], update.Applied);
        Assert.Equal(new ServerAtInstall(HasExited: true, ExitCode: 0), atInstall.Value);
    }

    [Fact]
    public async Task Quitting_without_a_downloaded_update_stops_the_server_and_installs_nothing()
    {
        var server = await _servers.StartAsync(ServerFolder());
        var update = new FakeUpdateService();

        await Shutdown(server, update).QuitAsync();

        Assert.Empty(update.Applied);
        Assert.True(server.HasExited);
        Assert.Equal(0, server.ExitCode);
    }

    [Fact]
    public async Task Restarting_to_update_stops_the_server_cleanly_before_the_update_is_installed()
    {
        var server = await _servers.StartAsync(ServerFolder());
        var update = new FakeUpdateService().AlreadyDownloaded();
        var atInstall = ObserveServerWhenInstallStarts(update, server);

        await Shutdown(server, update).RestartToUpdateAsync();

        Assert.Equal([FakeUpdateService.AppliedAndRestarted], update.Applied);
        Assert.Equal(new ServerAtInstall(HasExited: true, ExitCode: 0), atInstall.Value);
    }

    [Fact]
    public async Task Restarting_to_update_with_nothing_downloaded_leaves_the_server_running()
    {
        var server = await _servers.StartAsync(ServerFolder());
        var update = new FakeUpdateService();

        await Shutdown(server, update).RestartToUpdateAsync();

        Assert.Empty(update.Applied);
        Assert.False(server.HasExited);
    }

    private static TrayShutdown Shutdown(Process server, FakeUpdateService update) =>
        new(() => ServerProcessStop.StopAsync(server), update);

    private static Observed<ServerAtInstall> ObserveServerWhenInstallStarts(FakeUpdateService update, Process server)
    {
        var observed = new Observed<ServerAtInstall>();
        update.OnApply = _ => observed.Value = new ServerAtInstall(server.HasExited, server.HasExited ? server.ExitCode : null);
        return observed;
    }

    private string ServerFolder() => Path.Combine(_home.Path, "server-" + Guid.NewGuid().ToString("n"));

    // A stand-in that was asked to stop exits with 0; one that was killed does not.
    private sealed record ServerAtInstall(bool HasExited, int? ExitCode);

    private sealed class Observed<T>
    {
        public T? Value { get; set; }
    }
}
