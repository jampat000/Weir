using Microsoft.Extensions.Time.Testing;

namespace Weir.Infrastructure.Tests;

/// <summary>Reads the existing call sites' <c>clock.Set(now)</c> as the standard <see cref="FakeTimeProvider"/>'s own <c>SetUtcNow</c>.</summary>
internal static class FakeTimeProviderExtensions
{
    public static void Set(this FakeTimeProvider clock, DateTimeOffset now) => clock.SetUtcNow(now);
}
