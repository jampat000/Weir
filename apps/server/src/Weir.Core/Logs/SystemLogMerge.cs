namespace Weir.Core.Logs;

/// <summary>What one source found for a read of the log.</summary>
/// <param name="Rows">Its first rows after the cursor, in the order asked for: one more than a page when it has that many.</param>
/// <param name="Matching">How many rows it holds that pass every filter but the source's own.</param>
/// <param name="Levels">Its rows by level, with every filter but the level applied.</param>
/// <param name="Categories">Its rows by category, with every filter but the category applied.</param>
public sealed record SystemLogSlice(
    IReadOnlyList<SystemLogRow> Rows,
    long Matching,
    IReadOnlyDictionary<string, long> Levels,
    IReadOnlyDictionary<string, long> Categories)
{
    /// <summary>What a source that cannot hold a matching row (or was not asked) reports.</summary>
    public static readonly SystemLogSlice None = new([], 0, new Dictionary<string, long>(), new Dictionary<string, long>());
}

/// <summary>Merges what each source found into one page of the log, and totals the counts the filter chips show.</summary>
public static class SystemLogMerge
{
    /// <summary>
    /// The first <paramref name="limit"/> rows of every slice together, in <paramref name="order"/>, and a cursor when more remain.
    /// Each slice holds its own first rows, so the first of the union is the first of all; one row over the limit says there is
    /// another page. <paramref name="workflowNames"/> names the workflows of the rows, for an order that goes by name.
    /// </summary>
    public static SystemLogPage Page(
        IReadOnlyDictionary<SystemLogSource, SystemLogSlice> slices,
        SystemLogFilter filter,
        SystemLogOrder order,
        IReadOnlyDictionary<long, string> workflowNames,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(slices);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(workflowNames);
        var selected = SystemLogSources.All.Where(filter.Selects).ToList();
        var keyed = selected
            .SelectMany(source => slices[source].Rows)
            .Select(row => (Row: row, Key: order.KeyOf(row, workflowNames)))
            .OrderBy(pair => pair.Key, Comparer<IReadOnlyList<object?>>.Create(order.Compare))
            .Take(limit + 1)
            .ToList();
        var more = keyed.Count > limit;
        if (more)
        {
            keyed.RemoveAt(limit);
        }

        var rows = keyed.Select(pair => pair.Row).ToList();
        var counts = new SystemLogCounts(
            SystemLogSources.All.ToDictionary(SystemLogSources.NameOf, source => slices[source].Matching),
            Sum(SystemLogLevels.All, selected.Select(source => slices[source].Levels)),
            Sum(SystemLogCategories.All, selected.Select(source => slices[source].Categories)));
        return new SystemLogPage(
            rows,
            more ? order.EncodeCursor(keyed[^1].Key) : null,
            selected.Sum(source => slices[source].Matching),
            counts);
    }

    private static Dictionary<string, long> Sum(IReadOnlyList<string> keys, IEnumerable<IReadOnlyDictionary<string, long>> tallies)
    {
        var totals = keys.ToDictionary(key => key, _ => 0L);
        foreach (var tally in tallies)
        {
            foreach (var (key, count) in tally)
            {
                totals[key] += count;
            }
        }

        return totals;
    }
}
