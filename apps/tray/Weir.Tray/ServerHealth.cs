using System.Net;

namespace Weir.Tray;

/// <summary>Waits for a starting server to answer its readiness endpoint.</summary>
static class ServerHealth
{
    /// <summary>How long one readiness request may take before it counts as "not ready yet".</summary>
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    /// <summary>The pause between attempts, so a server that answers "not ready" quickly is not hammered.</summary>
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>How long to keep asking, how long to pause between attempts, and the clock both are measured on.</summary>
    internal sealed record Timing(TimeSpan Timeout, TimeSpan RetryDelay, TimeProvider Clock);

    /// <summary>
    /// Returns once <paramref name="readyUrl"/> answers 200. Throws <see cref="InvalidOperationException"/> as soon
    /// as the server process has exited, and <see cref="TimeoutException"/> (never a network error) once the
    /// timeout has passed without a 200. <paramref name="exitCodeIfExited"/> returns the server's exit code once it
    /// has exited, and null while it runs.
    /// </summary>
    internal static async Task WaitUntilReadyAsync(
        HttpClient client,
        Uri readyUrl,
        Func<int?> exitCodeIfExited,
        Timing timing,
        CancellationToken cancellationToken)
    {
        var started = timing.Clock.GetTimestamp();
        while (true)
        {
            if (exitCodeIfExited() is { } exitCode)
            {
                throw new InvalidOperationException(
                    $"Weir server process exited unexpectedly with code {exitCode} before becoming healthy.");
            }

            var answer = await AskAsync(client, readyUrl, cancellationToken).ConfigureAwait(false);
            if (answer is null)
            {
                return;
            }

            if (timing.Clock.GetElapsedTime(started) >= timing.Timeout)
            {
                throw new TimeoutException(
                    $"Weir did not answer {readyUrl} within {timing.Timeout.TotalSeconds:0.#}s (last attempt: {answer}).");
            }
            await Task.Delay(timing.RetryDelay, timing.Clock, cancellationToken).ConfigureAwait(false);
        }
    }

    // Null when the server is ready; otherwise what it (or the network) said instead.
    private static async Task<string?> AskAsync(HttpClient client, Uri readyUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client
                .GetAsync(readyUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK ? null : $"HTTP {(int)response.StatusCode}";
        }
        catch (HttpRequestException ex)
        {
            // Nothing listening yet, or the connection dropped while the server started.
            return ex.Message;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"no answer within {client.Timeout.TotalSeconds:0.#}s";
        }
    }
}
