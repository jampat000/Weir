using System.Net;

namespace Weir.Tray;

/// <summary>Waits for a starting server to answer its readiness endpoint.</summary>
static class ServerHealth
{
    /// <summary>How long one readiness request may take before it counts as "not ready yet".</summary>
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    /// <summary>The pause between attempts, so a server that answers "not ready" quickly is not hammered.</summary>
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How long to keep asking while nothing answers, how long to pause between attempts, and the clock both are measured on. Once
    /// the server has answered with anything but 200 it is alive and starting, and <paramref name="WhenStarting"/>, when given and
    /// longer, is how long it is then kept waiting for. While <paramref name="StillWorking"/> says yes the wait goes on past either
    /// limit: it is how a server that is alive and has said it is busy (saving a copy of the data before an update) is not given up
    /// on, for as long as its own note stays fresh.
    /// </summary>
    internal sealed record Timing(
        TimeSpan Timeout,
        TimeSpan RetryDelay,
        TimeProvider Clock,
        TimeSpan? WhenStarting = null,
        Func<bool>? StillWorking = null);

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
        var limit = timing.Timeout;
        while (true)
        {
            if (exitCodeIfExited() is { } exitCode)
            {
                throw new InvalidOperationException(
                    $"Weir server process exited unexpectedly with code {exitCode} before becoming healthy.");
            }

            var (answer, serverAnswered) = await AskAsync(client, readyUrl, cancellationToken).ConfigureAwait(false);
            if (answer is null)
            {
                return;
            }

            if (serverAnswered && timing.WhenStarting is { } whenStarting && whenStarting > limit)
            {
                limit = whenStarting;
            }

            if (timing.Clock.GetElapsedTime(started) >= limit && timing.StillWorking?.Invoke() != true)
            {
                throw new TimeoutException(
                    $"Weir did not answer {readyUrl} within {limit.TotalSeconds:0.#}s (last attempt: {answer}).");
            }
            await Task.Delay(timing.RetryDelay, timing.Clock, cancellationToken).ConfigureAwait(false);
        }
    }

    // Null when the server is ready; otherwise what it (or the network) said instead, and whether the server itself said it.
    private static async Task<(string? Answer, bool ServerAnswered)> AskAsync(HttpClient client, Uri readyUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client
                .GetAsync(readyUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK ? (null, true) : ($"HTTP {(int)response.StatusCode}", true);
        }
        catch (HttpRequestException ex)
        {
            // Nothing listening yet, or the connection dropped while the server started.
            return (ex.Message, false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ($"no answer within {client.Timeout.TotalSeconds:0.#}s", false);
        }
    }
}
