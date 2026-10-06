using Xunit.Sdk;

namespace Weir.Contract.Tests.Libraries;

/// <summary>Watching that something does not happen. Only for claims no API can answer; prefer asserting on state the server reports.</summary>
internal static class LibrariesPartBWaits
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    /// <summary>Calls <paramref name="happened"/> for <paramref name="duration"/> and fails as soon as it returns true.</summary>
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
