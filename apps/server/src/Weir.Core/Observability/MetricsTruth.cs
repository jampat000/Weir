using Weir.Core.Json;

namespace Weir.Core.Observability;

/// <summary>Guards for reported metric counts: never negative.</summary>
public static class MetricsTruth
{
    public static void RequireNonNegative(IReadOnlyDictionary<string, long> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        foreach (var (name, value) in counts)
        {
            if (value < 0)
            {
                throw new WireValueException($"Metric {WireStrings.Repr(name)} must not be negative.");
            }
        }
    }

    public static long FinalizedSuccessTotal(IReadOnlyDictionary<string, long> counts)
    {
        RequireNonNegative(counts);
        return counts.Values.Sum();
    }
}
