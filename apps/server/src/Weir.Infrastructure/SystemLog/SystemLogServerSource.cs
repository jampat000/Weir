using System.Globalization;
using Microsoft.Extensions.Logging;
using Weir.Core.Logs;
using Weir.Core.Time;
using Weir.Infrastructure.Logging;

namespace Weir.Infrastructure.SystemLog;

/// <summary>
/// The lines of the server log file in System › Logs. The file is read once through, tallying every line that passes
/// the filters and keeping only the newest page of them, so the work grows with the file but the memory does not.
/// </summary>
internal sealed class SystemLogServerSource
{
    private const string UtcMarker = "Z";
    private const string UtcOffset = "+00:00";

    private readonly WeirLogFile _file;

    public SystemLogServerSource(WeirLogFile file) => _file = file ?? throw new ArgumentNullException(nameof(file));

    public Task<SystemLogSlice> ReadAsync(SystemLogRequest request, ILogger logger)
    {
        var filter = request.Filter;
        return filter.CanMatch(SystemLogSource.Server) && File.Exists(_file.Path)
            ? Task.Run(() => Scan(request, logger))
            : Task.FromResult(SystemLogSlice.None);
    }

    private SystemLogSlice Scan(SystemLogRequest request, ILogger logger)
    {
        var filter = request.Filter;
        var selected = filter.Selects(SystemLogSource.Server);
        var text = filter.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var jobId = filter.JobId?.ToString(CultureInfo.InvariantCulture);
        var matching = 0L;
        var levels = new Dictionary<string, long>();
        var categories = new Dictionary<string, long>();
        var newest = new SortedSet<SystemLogRow>(Comparer<SystemLogRow>.Create((first, second) => SystemLogPosition.NewestFirst.Compare(first.Position, second.Position)));
        var lineNumber = 0L;

        void Visit(string rawLine)
        {
            lineNumber++;
            if (SuiteLogFilter.Parse(rawLine) is not { } entry || SuiteLogFilter.SkipLowValueNoise(entry) || RowOf(entry, lineNumber) is not { } row)
            {
                return;
            }

            if (!PassesOtherFilters(filter, entry, row, text, jobId))
            {
                return;
            }

            var levelOk = filter.Levels.Count == 0 || filter.Levels.Contains(row.Level);
            var categoryOk = filter.Categories.Count == 0 || filter.Categories.Contains(row.Category);
            if (categoryOk)
            {
                Increment(levels, row.Level);
            }

            if (levelOk)
            {
                Increment(categories, row.Category);
            }

            if (!levelOk || !categoryOk)
            {
                return;
            }

            matching++;
            if (selected && (request.After is not { } after || row.Position.IsAfter(after)))
            {
                KeepNewest(newest, row, request.Take);
            }
        }

        if (!_file.ReadLines(Visit))
        {
            logger.LogWarning("The server log could not be opened to list it in System › Logs.");
        }

        return selected
            ? new SystemLogSlice([.. newest], matching, levels, categories)
            : SystemLogSlice.None with { Matching = matching };
    }

    private static bool PassesOtherFilters(SystemLogFilter filter, ParsedLogEntry entry, SystemLogRow row, string loweredText, string? jobId) =>
        (filter.From is not { } from || row.At >= from)
        && (filter.To is not { } to || row.At <= to)
        && (jobId is null || entry.JobId == jobId)
        && (filter.HasException is not { } wanted || wanted == !string.IsNullOrEmpty(entry.Traceback))
        && (loweredText.Length == 0 || SuiteLogFilter.ContainsText(entry, loweredText));

    private static void KeepNewest(SortedSet<SystemLogRow> newest, SystemLogRow row, int capacity)
    {
        newest.Add(row);
        if (newest.Count > capacity)
        {
            newest.Remove(newest.Max!);
        }
    }

    private static void Increment(Dictionary<string, long> tally, string key) =>
        tally[key] = tally.GetValueOrDefault(key) + 1;

    /// <summary>The line as a row, or null when its time cannot be read, as the Logs list has always left such a line out.</summary>
    private static SystemLogRow? RowOf(ParsedLogEntry entry, long lineNumber)
    {
        if (SuiteLogFilter.ToOut(entry) is not { } record
            || !Timestamp.TryFromIsoFormat(entry.Timestamp.Replace(UtcMarker, UtcOffset, StringComparison.Ordinal), out var at))
        {
            return null;
        }

        return new SystemLogRow(
            SystemLogSource.Server,
            lineNumber,
            new DateTimeOffset(at.AsUtc, TimeSpan.Zero),
            SystemLogRules.ServerLevel(entry.Level),
            SystemLogRules.ServerCategory(entry.Logger),
            null,
            entry.Message,
            entry.Detail ?? FirstLine(entry.Traceback),
            record);
    }

    private static string? FirstLine(string? text) =>
        text?.Split('\n', 2)[0].Trim() is { Length: > 0 } line ? line : null;
}
