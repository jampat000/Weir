namespace Weir.Infrastructure.Tests;

/// <summary>
/// Polls a condition until it is true or a ceiling elapses. Used to observe the effect of a signal or a
/// <c>FakeTimeProvider.Advance</c> call after the fact (the condition itself never depends on wall-clock
/// time), never as a substitute for a real completion signal where one is available.
/// </summary>
internal static class Eventually
{
    private static readonly TimeSpan DefaultCeiling = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    public static Task ThatAsync(Func<bool> condition, TimeSpan? ceiling = null) =>
        ThatAsync(() => Task.FromResult(condition()), ceiling);

    public static async Task ThatAsync(Func<Task<bool>> condition, TimeSpan? ceiling = null)
    {
        var deadline = DateTime.UtcNow + (ceiling ?? DefaultCeiling);
        while (!await condition().ConfigureAwait(false))
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Condition was not met in time.");
            }

            await Task.Delay(PollInterval).ConfigureAwait(false);
        }
    }
}
