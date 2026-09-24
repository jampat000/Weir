namespace Weir.Api.Tests;

/// <summary>
/// Polls a condition until it is true or a ceiling elapses, replacing the ad hoc polling loops that used to
/// be copied into each startup/endpoint test.
/// </summary>
internal static class Eventually
{
    private static readonly TimeSpan DefaultCeiling = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    public static Task ThatAsync(Func<bool> condition, TimeSpan? ceiling = null, string? message = null) =>
        ThatAsync(() => Task.FromResult(condition()), ceiling, message);

    public static async Task ThatAsync(Func<Task<bool>> condition, TimeSpan? ceiling = null, string? message = null)
    {
        var deadline = DateTime.UtcNow + (ceiling ?? DefaultCeiling);
        while (!await condition().ConfigureAwait(false))
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail(message ?? "Condition was not met in time.");
            }

            await Task.Delay(PollInterval).ConfigureAwait(false);
        }
    }

    /// <summary>Polls until <paramref name="isDone"/> accepts the polled value, and returns that value either
    /// way - the caller asserts on it, so a timeout still fails with the value's own mismatch rather than a
    /// generic "took too long" message.</summary>
    public static async Task<T> PollAsync<T>(Func<Task<T>> poll, Func<T, bool> isDone, TimeSpan? ceiling = null)
    {
        var deadline = DateTime.UtcNow + (ceiling ?? DefaultCeiling);
        while (true)
        {
            var value = await poll().ConfigureAwait(false);
            if (isDone(value) || DateTime.UtcNow > deadline)
            {
                return value;
            }

            await Task.Delay(PollInterval).ConfigureAwait(false);
        }
    }
}
