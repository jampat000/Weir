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

    /// <summary>Starts a stand-in in <paramref name="directory"/> and returns once it has created its stop event.</summary>
    public async Task<Process> StartAsync(string directory, params string[] arguments)
    {
        Directory.CreateDirectory(directory);
        foreach (var file in StandInServerFiles)
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, file), Path.Combine(directory, file));
        }
        var exe = Path.Combine(directory, "WeirServer.exe");
        File.Move(Path.Combine(directory, StandInServerName + ".exe"), exe);

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
