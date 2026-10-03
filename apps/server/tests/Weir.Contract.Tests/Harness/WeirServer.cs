using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace Weir.Contract.Tests.Harness;

/// <summary>
/// One real Weir server process with its own data folder and its own port, so any number of them run side by
/// side. Started from the built server, never in-process. Stopping it takes down only the process this class
/// started (and what that process started). The data folder survives a restart; a restart picks a new port, so
/// create clients after it.
/// </summary>
public sealed class WeirServer : IAsyncDisposable
{
    public const string KeepDataVariable = "WEIR_CONTRACT_KEEP_DATA";

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ReadyPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ReadyProbeTimeout = TimeSpan.FromSeconds(2);
    private static readonly HttpClient ReadyProbe = new() { Timeout = ReadyProbeTimeout };

    private readonly ServerBinary _binary;
    private readonly Dictionary<string, string> _environment;
    private readonly List<ServerLog> _logs = [];
    private Process? _process;

    private WeirServer(ServerBinary binary, string home, Dictionary<string, string> environment)
    {
        _binary = binary;
        Home = home;
        _environment = environment;
    }

    public string Home { get; }

    public Uri BaseUrl { get; private set; } = new("http://127.0.0.1/");

    public string DatabasePath => Path.Combine(Home, "data", "weir.sqlite3");

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>Starts a server with a fresh data folder and waits until it reports ready.</summary>
    public static async Task<WeirServer> StartNewAsync(IReadOnlyDictionary<string, string>? environment = null)
    {
        var binary = ServerBinary.Locate();
        ServerLedger.Shared.StopOrphans();
        var home = Directory.CreateTempSubdirectory("weir_contract_").FullName;
        var settings = new Dictionary<string, string>(environment ?? new Dictionary<string, string>());
        var server = new WeirServer(binary, home, settings);
        try
        {
            await server.LaunchAsync();
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }

        return server;
    }

    public WeirClient CreateClient(IReadOnlyDictionary<string, string>? defaultHeaders = null) => new(BaseUrl, defaultHeaders);

    /// <summary>A client signed in as the admin, created through bootstrap the first time.</summary>
    public async Task<WeirClient> CreateAdminClientAsync(IReadOnlyDictionary<string, string>? defaultHeaders = null)
    {
        var client = CreateClient(defaultHeaders);
        try
        {
            await client.EnsureAdminAsync();
        }
        catch
        {
            client.Dispose();
            throw;
        }

        return client;
    }

    public async Task RestartAsync(IReadOnlyDictionary<string, string>? environmentChanges = null)
    {
        await StopAsync();
        foreach (var (name, value) in environmentChanges ?? new Dictionary<string, string>())
        {
            _environment[name] = value;
        }

        await LaunchAsync();
    }

    /// <summary>Stops the server and what it started. The data folder stays.</summary>
    public async Task StopAsync()
    {
        var process = _process;
        _process = null;
        if (process is null)
        {
            return;
        }

        var processId = process.Id;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync();
        }
        finally
        {
            ServerLedger.Shared.Forget(processId);
            process.Dispose();
        }
    }

    /// <summary>Stops the server, opens its SQLite file for seeding or inspection, and restarts it when disposed.</summary>
    public async Task<StoppedDatabase> StopForDatabaseAsync()
    {
        var wasRunning = IsRunning;
        await StopAsync();
        return await StoppedDatabase.OpenAsync(DatabasePath, wasRunning ? LaunchAsync : () => Task.CompletedTask);
    }

    public string LogText() => string.Join(Environment.NewLine, _logs.Select(log => log.Text()));

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        foreach (var log in _logs)
        {
            log.Dispose();
        }

        if (Environment.GetEnvironmentVariable(KeepDataVariable) != "1")
        {
            Directory.Delete(Home, recursive: true);
        }
    }

    private async Task LaunchAsync()
    {
        using var startPlace = await ServerStartGate.EnterAsync();
        var port = FreePort();
        BaseUrl = new Uri($"http://127.0.0.1:{port}/");
        var log = new ServerLog(Path.Combine(Home, "contract-logs", $"server-{_logs.Count + 1}.log"));
        _logs.Add(log);

        var (program, leadingArguments) = _binary.Command();
        var start = new ProcessStartInfo(program)
        {
            WorkingDirectory = _binary.WorkingFolder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var listenArguments = new[] { "--host", "127.0.0.1", "--port", port.ToString(CultureInfo.InvariantCulture) };
        foreach (var argument in leadingArguments.Concat(listenArguments))
        {
            start.ArgumentList.Add(argument);
        }

        ServerEnvironment.Apply(start.Environment, Home, BaseUrl, _environment);

        _process = Process.Start(start) ?? throw new InvalidOperationException($"{program} did not start.");
        log.Follow(_process);
        ServerLedger.Shared.Record(_process);
        await WaitUntilReadyAsync(_process, log);
    }

    private async Task WaitUntilReadyAsync(Process process, ServerLog log)
    {
        var deadline = DateTime.UtcNow + StartTimeout;
        var lastProblem = "no answer yet";
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"The Weir server exited with code {process.ExitCode} before it was ready.{Environment.NewLine}Last lines of its log ({log.Path}):{Environment.NewLine}{log.Tail()}");
            }

            var problem = await ProbeReadyAsync();
            if (problem is null)
            {
                return;
            }

            lastProblem = problem;

            await Task.Delay(ReadyPollInterval);
        }

        throw new TimeoutException(
            $"The Weir server did not report ready at {BaseUrl}ready within {StartTimeout.TotalSeconds:0}s ({lastProblem}).{Environment.NewLine}Last lines of its log ({log.Path}):{Environment.NewLine}{log.Tail()}");
    }

    /// <summary>Null when the server is ready, otherwise what it answered instead.</summary>
    private async Task<string?> ProbeReadyAsync()
    {
        try
        {
            using var response = await ReadyProbe.GetAsync(new Uri(BaseUrl, "ready"));
            var body = await response.Content.ReadAsStringAsync();
            var ready = response.StatusCode == HttpStatusCode.OK && (bool?)JsonNode.Parse(body)?["ready"] == true;
            return ready ? null : $"HTTP {(int)response.StatusCode} {body}";
        }
        catch (HttpRequestException problem)
        {
            return problem.Message;
        }
        catch (TaskCanceledException)
        {
            return "no answer within the probe timeout";
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
