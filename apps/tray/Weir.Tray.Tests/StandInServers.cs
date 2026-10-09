using System.Diagnostics;

using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// Starts copies of Weir.Tray.StandInServer named WeirServer.exe and kills whatever is still running on dispose.
/// The stand-in is a Windows-subsystem program, so unlike a system console program it can never open a console
/// window (#821). Its apphost looks for the assembly and runtime config beside itself, so a renamed copy needs all three.
/// </summary>
internal sealed class StandInServers : IDisposable
{
    private const string StandInServerName = "Weir.Tray.StandInServer";
    private const string ReadyLine = "ready";

    private static readonly string[] StandInServerFiles = [StandInServerName + ".exe", StandInServerName + ".dll", StandInServerName + ".runtimeconfig.json"];
    private static readonly TimeSpan ReadyCeiling = TimeSpan.FromSeconds(30);

    private readonly List<Process> _started = [];

    /// <summary>Starts a stand-in server in <paramref name="directory"/> and returns once it has created its stop event.</summary>
    public Task<Process> StartAsync(string directory, params string[] arguments) =>
        StartNamedAsync(directory, "WeirServer.exe", arguments);

    /// <summary>
    /// Starts a stand-in for the tray, named Weir.exe, in <paramref name="directory"/>. It has no stop event and stays
    /// alive until it is killed, as the real tray does.
    /// </summary>
    public Task<Process> StartTrayAsync(string directory) =>
        StartNamedAsync(directory, "Weir.exe", ["no-stop-event"]);

    /// <summary>Puts a stand-in server in <paramref name="directory"/> as <paramref name="executableName"/> without starting it, and returns its path.</summary>
    internal static string Place(string directory, string executableName)
    {
        Directory.CreateDirectory(directory);
        foreach (var file in StandInServerFiles)
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, file), Path.Combine(directory, file));
        }
        var exe = Path.Combine(directory, executableName);
        File.Move(Path.Combine(directory, StandInServerName + ".exe"), exe);
        return exe;
    }

    private async Task<Process> StartNamedAsync(string directory, string executableName, string[] arguments)
    {
        var exe = Place(directory, executableName);

        var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        var process = Process.Start(start)!;
        _started.Add(process);
        var announced = await process.StandardOutput.ReadLineAsync().WaitAsync(ReadyCeiling);
        Assert.Equal(ReadyLine, announced);
        return process;
    }

    public void Dispose()
    {
        foreach (var process in _started)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                process.WaitForExit(5_000);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone, which is what this clean-up wants.
            }
            process.Dispose();
        }
    }
}
