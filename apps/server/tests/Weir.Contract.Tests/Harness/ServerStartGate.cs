namespace Weir.Contract.Tests.Harness;

/// <summary>
/// Limits how many servers start at the same moment. xunit runs test classes side by side, and each class starts a
/// server of its own; on a small machine a dozen servers all starting at once slow each other down until some miss
/// their readiness deadline. Only the start is limited: a server that is ready gives its place back and keeps
/// running, so classes still run side by side once their servers are up.
/// </summary>
internal static class ServerStartGate
{
    private const int MinimumStartsAtOnce = 2;

    private static readonly SemaphoreSlim Places = new(Math.Max(MinimumStartsAtOnce, Environment.ProcessorCount));

    /// <summary>Waits for a place to start in; dispose the result once the server is ready (or failed to start).</summary>
    public static async Task<IDisposable> EnterAsync()
    {
        await Places.WaitAsync();
        return new Place();
    }

    private sealed class Place : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Places.Release();
            }
        }
    }
}
