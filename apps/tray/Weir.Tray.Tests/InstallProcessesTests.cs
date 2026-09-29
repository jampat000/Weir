using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// A machine can run more than one Weir, so the install and uninstall hooks stop only this install's own
/// Weir and WeirServer processes, never every process with those names.
/// </summary>
public sealed class InstallProcessesTests : IDisposable
{
    private readonly TempDirectory _root = TempDirectory.Create();
    private readonly StandInServers _servers = new();

    public void Dispose()
    {
        _servers.Dispose();
        _root.Dispose();
    }

    [Theory]
    [InlineData(@"C:\Users\a\AppData\Local\Weir\current\Weir.exe", @"C:\Users\a\AppData\Local\Weir\current", true)]
    [InlineData(@"C:\Users\a\AppData\Local\Weir\current\server\WeirServer.exe", @"C:\Users\a\AppData\Local\Weir\current\", true)]
    [InlineData(@"c:\users\A\appdata\local\weir\CURRENT\server\weirserver.exe", @"C:\Users\a\AppData\Local\Weir\current", true)]
    [InlineData(@"C:\Deluno\Weir\app\current\server\WeirServer.exe", @"C:\Users\a\AppData\Local\Weir\current", false)]
    [InlineData(@"C:\Weir-old\Weir.exe", @"C:\Weir", false)]
    [InlineData(@"C:\Weir\..\Other\Weir.exe", @"C:\Weir", false)]
    [InlineData(@"C:\Weir", @"C:\Weir", false)]
    [InlineData(null, @"C:\Weir", false)]
    [InlineData("", @"C:\Weir", false)]
    public void Only_paths_inside_the_install_count(string? executable, string root, bool expected)
    {
        Assert.Equal(expected, InstallProcesses.IsInside(executable, root));
    }

    /// <summary>
    /// A real process named WeirServer inside the install root, and another outside it. Only the
    /// one inside is stopped. (The stand-in server, copied and renamed, stands in for the real one.)
    /// </summary>
    [Fact]
    public async Task Stops_this_installs_server_and_leaves_another_weir_running()
    {
        var install = Path.Combine(_root.Path, "install", "current");
        var elsewhere = Path.Combine(_root.Path, "Deluno", "Weir", "app", "current");
        var inside = await _servers.StartAsync(Path.Combine(install, "server"));
        var outside = await _servers.StartAsync(Path.Combine(elsewhere, "server"));
        var log = new List<string>();

        var stopped = InstallProcesses.StopOwn(install, sameSessionOnly: false, log.Add, "test");

        Assert.Contains(inside.Id, stopped);
        Assert.DoesNotContain(outside.Id, stopped);
        Assert.True(inside.WaitForExit(5_000), "the install's own server should have been stopped");
        Assert.False(outside.HasExited, "a Weir outside this install must be left running");
        Assert.Contains(log, line => line.Contains($"pid {outside.Id}") && line.Contains("not part of this install"));
    }

    [Fact]
    public async Task Asks_this_installs_server_to_stop_before_killing_it()
    {
        var install = Path.Combine(_root.Path, "install", "current");
        var server = await _servers.StartAsync(Path.Combine(install, "server"));
        var log = new List<string>();

        InstallProcesses.StopOwn(install, sameSessionOnly: false, log.Add, "test");

        Assert.True(server.WaitForExit(5_000), "the install's own server should have been stopped");
        Assert.Equal(0, server.ExitCode);
        Assert.Contains(log, line => line.Contains($"pid {server.Id}") && line.Contains("cleanly"));
    }

    [Fact]
    public async Task Kills_this_installs_server_when_it_cannot_be_asked_to_stop()
    {
        var install = Path.Combine(_root.Path, "install", "current");
        var server = await _servers.StartAsync(Path.Combine(install, "server"), "no-stop-event");
        var log = new List<string>();

        var stopped = InstallProcesses.StopOwn(install, sameSessionOnly: false, log.Add, "test");

        Assert.Contains(server.Id, stopped);
        Assert.True(server.WaitForExit(5_000), "the install's own server should have been stopped");
        Assert.DoesNotContain(log, line => line.Contains("cleanly"));
    }
}
