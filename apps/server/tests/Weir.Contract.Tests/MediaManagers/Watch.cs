using Xunit.Sdk;

namespace Weir.Contract.Tests.MediaManagers;

/// <summary>Watching for something that must not happen.</summary>
internal static class Watch
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Calls <paramref name="happened"/> for <paramref name="duration"/> and fails as soon as it returns true. Watching can only
    /// show that something did not happen yet, so prefer asserting on state the server reports when it has any; use this for
    /// claims no API can answer.
    /// </summary>
    public static async Task NeverWithinAsync(Func<bool> happened, TimeSpan duration, string what)
    {
        var deadline = DateTime.UtcNow + duration;
        while (true)
        {
            if (happened())
            {
                throw new XunitException($"{what} happened within {duration.TotalSeconds:0}s, but it must not.");
            }

            if (DateTime.UtcNow >= deadline)
            {
                return;
            }

            await Task.Delay(Interval);
        }
    }
}
