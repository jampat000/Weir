using Weir.Core.Configuration;

namespace Weir.Host;

/// <summary>
/// <c>WeirServer --healthcheck [--port N]</c>: asks this same server's own <c>/health</c> over
/// loopback and returns 0 or 1. Exists so the Docker image can run a healthcheck without an HTTP
/// client tool (curl) installed alongside the server — see the repo root Dockerfile's HEALTHCHECK.
/// </summary>
internal static class HealthCheckCommand
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

    public static async Task<int> RunAsync(IReadOnlyList<string> args, RuntimeEnvironment runtime, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(error);

        ServerListenOptions listen;
        try
        {
            listen = ServerListenOptions.Parse(args, runtime);
        }
        catch (WeirConfigurationException exception)
        {
            await error.WriteLineAsync($"Weir healthcheck cannot run: {exception.Message}").ConfigureAwait(false);
            return 1;
        }

        // Always loopback, regardless of --host: the healthcheck runs inside the same container as
        // the server it is checking, and the server binds every interface (or loopback) either way.
        using var client = new HttpClient { Timeout = RequestTimeout };
        try
        {
            using var response = await client.GetAsync($"http://127.0.0.1:{listen.Port}/health").ConfigureAwait(false);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }
}
