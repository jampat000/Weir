using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Weir.Tray;

/// <summary>
/// The bundled server process: starting it, waiting for it to be ready, restarting it when it exits unexpectedly,
/// moving it to another port, and stopping it. Nothing here touches the UI; the tray is told
/// about outcomes and shows them itself.
/// </summary>
sealed class ServerHost : IDisposable
{
    /// <summary>
    /// The port the server listens on at the moment, for a second launch that opens the running Weir
    /// (Program.OpenExistingInstanceBrowser). The saved choice is port.txt (PortChoice).
    /// </summary>
    internal const string CurrentPortFileName = "current-port.txt";

    private const string ServerExeName = "WeirServer.exe";
    private const int MaxRestarts = 5;

    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(3);

    /// <summary>How long a stop waits for a restart or port change in progress to let go of the server.</summary>
    private static readonly TimeSpan StopGateWait = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan[] RestartBackoff =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
    ];

    private readonly string _runtimeHome;
    private readonly string _installRoot;
    private readonly HttpClient _http = new() { Timeout = ServerHealth.RequestTimeout };

    // Held while the server is started, replaced or stopped, by the watchdog, a port change or a quit, so no two of
    // them act on the process at once.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private volatile Process? _process;
    private volatile int _port;
    private Task? _watchdog;
    private Action? _onGaveUp;
    private CancellationToken _watchdogToken;

    internal ServerHost(string runtimeHome, string installRoot, int port)
    {
        _runtimeHome = runtimeHome;
        _installRoot = installRoot;
        _port = port;
    }

    internal int Port => _port;

    /// <summary>Sets what every server process is started with (ServerEnvironment).</summary>
    internal void PrepareEnvironment() => ServerEnvironment.Apply(_runtimeHome, FindServerExeDirectory());

    internal void WritePortFile() =>
        File.WriteAllText(Path.Combine(_runtimeHome, CurrentPortFileName), _port.ToString(CultureInfo.InvariantCulture));

    /// <summary>Starts the server and returns once it is ready.</summary>
    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StartProcess();
            await WaitForHealthAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Watches the server and restarts it, with growing pauses, when it exits on its own. After
    /// <see cref="MaxRestarts"/> failed restarts in a row it gives up and calls <paramref name="onGaveUp"/>.
    /// </summary>
    internal void Watch(Action onGaveUp, CancellationToken cancellationToken)
    {
        _onGaveUp = onGaveUp;
        _watchdogToken = cancellationToken;
        LaunchWatchdog();
    }

    private void LaunchWatchdog()
    {
        var onGaveUp = _onGaveUp!;
        _watchdog = BackgroundWork.RunLoop("Server watchdog", ct => WatchAsync(onGaveUp, ct), _watchdogToken);
    }

    // A port change that brings the server back restarts a watchdog that stopped: it stops when it sees the process
    // cleared, and when it gives up after repeated failures.
    private void RestartWatchdogIfStopped()
    {
        if (_onGaveUp is null || _watchdogToken.IsCancellationRequested || _watchdog is { IsCompleted: false })
        {
            return;
        }
        LaunchWatchdog();
    }

    private async Task WatchAsync(Action onGaveUp, CancellationToken cancellationToken)
    {
        var failedRestarts = 0;
        using var timer = new PeriodicTimer(WatchInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var (state, exited) = await CheckProcessAsync(cancellationToken).ConfigureAwait(false);
            if (state == ProcessState.Stopped)
            {
                // Stopped on purpose (quit, update).
                return;
            }
            if (exited is null)
            {
                continue;
            }

            TrayLog.Write($"Bundled server host exited unexpectedly with code {exited.ExitCode} (restart {failedRestarts + 1}/{MaxRestarts})");
            if (failedRestarts >= MaxRestarts)
            {
                TrayLog.Write("Exceeded max restart attempts — giving up.");
                onGaveUp();
                return;
            }

            var delay = RestartBackoff[Math.Min(failedRestarts, RestartBackoff.Length - 1)];
            TrayLog.Write($"Waiting {delay.TotalMilliseconds:0}ms before restarting server...");
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            failedRestarts = await RestartAsync(exited, cancellationToken).ConfigureAwait(false) ? 0 : failedRestarts + 1;
        }
    }

    private enum ProcessState
    {
        Running,
        Exited,
        Stopped,
    }

    // The process comes back only when it has exited on its own.
    private async Task<(ProcessState State, Process? Exited)> CheckProcessAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var process = _process;
            return process switch
            {
                null => (ProcessState.Stopped, null),
                { HasExited: true } => (ProcessState.Exited, process),
                _ => (ProcessState.Running, null),
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    // Returns whether the server is back and healthy.
    private async Task<bool> RestartAsync(Process exited, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A port change may have replaced the server while the watchdog waited.
            if (!ReferenceEquals(_process, exited))
            {
                return true;
            }
            StartProcess();
            exited.Dispose();
            await WaitForHealthAsync(cancellationToken).ConfigureAwait(false);
            TrayLog.Write("Server restarted successfully.");
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or Win32Exception or FileNotFoundException)
        {
            TrayLog.Write($"Server restart failed: {ex.Message}");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Restarts the server on <paramref name="to"/> and saves it as the chosen port. If it does not come up there,
    /// goes back to the port it had, which was working a moment ago and is still the saved one. Returns whether
    /// the move succeeded.
    /// </summary>
    internal async Task<bool> MoveToPortAsync(int to, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var from = _port;
        try
        {
            TrayLog.Write($"Change port: restarting the server on port {to} (was {from}).");
            if (await TryStartOnAsync(to, cancellationToken).ConfigureAwait(false))
            {
                PortChoice.Save(_runtimeHome, to);
                WritePortFile();
                TrayLog.Write($"Change port: Weir is healthy on http://127.0.0.1:{to}/ and port {to} is saved.");
                return true;
            }
            TrayLog.Write($"Change port: going back to port {from}.");
            if (await TryStartOnAsync(from, cancellationToken).ConfigureAwait(false))
            {
                WritePortFile();
            }
            return false;
        }
        finally
        {
            _gate.Release();
            RestartWatchdogIfStopped();
        }
    }

    // Replaces the running server with one on this port. Called with the gate held.
    private async Task<bool> TryStartOnAsync(int port, CancellationToken cancellationToken)
    {
        await StopProcessAsync().ConfigureAwait(false);
        _port = port;
        try
        {
            StartProcess();
            await WaitForHealthAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or Win32Exception or FileNotFoundException)
        {
            TrayLog.Write($"The server did not start on port {port}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Stops the server, waiting briefly for a restart or port change in progress to let go of it first.</summary>
    internal async Task StopAsync()
    {
        var entered = await _gate.WaitAsync(StopGateWait).ConfigureAwait(false);
        try
        {
            await StopProcessAsync().ConfigureAwait(false);
        }
        finally
        {
            if (entered)
            {
                _gate.Release();
            }
        }
    }

    private async Task StopProcessAsync()
    {
        var process = _process;
        if (process is null)
        {
            return;
        }
        _process = null;
        using (process)
        {
            if (process.HasExited)
            {
                return;
            }
            TrayLog.Write($"Stopping bundled server host pid={process.Id}");
            process.CloseMainWindow();
            if (await ExitsWithinAsync(process, StopTimeout).ConfigureAwait(false))
            {
                return;
            }
            TrayLog.Write($"Bundled server host pid={process.Id} did not exit in time; killing it");
            try
            {
                process.Kill(entireProcessTree: true);
                await ExitsWithinAsync(process, KillWait).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
            {
                TrayLog.Write($"Could not kill the server process pid={process.Id}: {ex.Message}");
            }
        }
    }

    private static async Task<bool> ExitsWithinAsync(Process process, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void StartProcess()
    {
        var serverExe = FindServerExe();
        TrayLog.Write($"Starting bundled server host: {serverExe}");

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = serverExe,
            Arguments = string.Create(CultureInfo.InvariantCulture, $"--port {_port}"),
            WorkingDirectory = Path.GetDirectoryName(serverExe),
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException($"Failed to start {ServerExeName}");

        _process = process;
        TrayLog.Write($"Bundled server host pid={process.Id}");
    }

    private Task WaitForHealthAsync(CancellationToken cancellationToken)
    {
        var process = _process;
        return ServerHealth.WaitUntilReadyAsync(
            _http,
            new Uri($"http://127.0.0.1:{_port}/ready"),
            () => process is { HasExited: true } ? process.ExitCode : null,
            new ServerHealth.Timing(HealthTimeout, ServerHealth.RetryDelay, TimeProvider.System),
            cancellationToken);
    }

    private string FindServerExeDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(_installRoot, "server", ServerExeName),
            Path.Combine(_installRoot, ServerExeName),
            Path.Combine(_installRoot, "..", ServerExeName),
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(Path.GetFullPath(candidate))!;
            }
        }
        return _installRoot;
    }

    private string FindServerExe()
    {
        var exe = Path.Combine(FindServerExeDirectory(), ServerExeName);
        return File.Exists(exe) ? exe : throw new FileNotFoundException("Bundled server host is missing.", exe);
    }

    // Stopping the server is always explicit (quit, update, port change). A start-up whose health wait timed out
    // leaves the server running, and the next tray start stops it as an orphan (Program.StopOrphanedServers).
    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
