using Weir.Core.Json;
using Weir.Core.Time;

namespace Weir.Core.Logs;

/// <summary>One parsed line of <c>weir.log</c>.</summary>
public sealed record ParsedLogEntry(
    string Timestamp,
    string Level,
    string Component,
    string Message,
    string? Detail,
    string? Traceback,
    string? Source,
    string Logger,
    string? CorrelationId,
    string? JobId);

/// <summary>The result of reading the log for the Logs screen.</summary>
public sealed record SuiteLogsResult(IReadOnlyList<ParsedLogEntry> Items, long Total, long Errors, long Warnings, long Information);

/// <summary>Filters parsed log lines for the Logs screen and counts them by level.</summary>
public sealed class SuiteLogFilter
{
    private readonly string? _level;
    private readonly string _search;
    private readonly bool? _hasException;
    private readonly Queue<ParsedLogEntry> _rows = new();
    private readonly int _capacity;
    private long _total;
    private long _errors;
    private long _warnings;
    private long _information;

    public SuiteLogFilter(string? level, string? search, bool? hasException, long limit)
    {
        var requested = (level ?? string.Empty).Trim().ToUpperInvariant();
        _level = requested.Length == 0 ? null : requested;
        _search = (search ?? string.Empty).Trim().ToLowerInvariant();
        _hasException = hasException;
        _capacity = (int)Math.Max(1, Math.Min(limit, 250));
    }

    public static SuiteLogsResult Empty => new([], 0, 0, 0, 0);

    public void Add(string rawLine)
    {
        var entry = Parse(rawLine);
        if (entry is null || SkipLowValueNoise(entry))
        {
            return;
        }

        _total++;
        switch (entry.Level)
        {
            case "ERROR" or "CRITICAL":
                _errors++;
                break;
            case "WARNING":
                _warnings++;
                break;
            default:
                _information++;
                break;
        }

        if (_level is not null && entry.Level != _level)
        {
            return;
        }

        if (_hasException == true && string.IsNullOrEmpty(entry.Traceback))
        {
            return;
        }

        if (_hasException == false && !string.IsNullOrEmpty(entry.Traceback))
        {
            return;
        }

        if (_search.Length > 0)
        {
            var haystack = string.Join(
                " ",
                new[]
                {
                    entry.Message, entry.Detail ?? string.Empty, entry.Traceback ?? string.Empty, entry.Logger,
                    entry.Source ?? string.Empty, entry.Component, entry.CorrelationId ?? string.Empty, entry.JobId ?? string.Empty,
                }.Where(part => part.Length > 0)).ToLowerInvariant();
            if (!haystack.Contains(_search, StringComparison.Ordinal))
            {
                return;
            }
        }

        _rows.Enqueue(entry);
        if (_rows.Count > _capacity)
        {
            _rows.Dequeue();
        }
    }

    public SuiteLogsResult Result() => new([.. _rows.Reverse()], _total, _errors, _warnings, _information);

    /// <summary>One JSON log line, or <see langword="null"/> when it does not parse or has no timestamp or message.</summary>
    public static ParsedLogEntry? Parse(string raw)
    {
        WireValue payload;
        try
        {
            payload = WireJsonParser.Parse(raw);
        }
        catch (WireJsonDecodeException)
        {
            return null;
        }

        if (payload is not WireObject dict)
        {
            return null;
        }

        var timestamp = OrText(dict.Get("timestamp"), string.Empty).Trim();
        var level = OrText(dict.Get("level"), "INFO").Trim().ToUpperInvariant();
        if (level.Length == 0)
        {
            level = "INFO";
        }

        var logger = OrText(dict.Get("logger"), "weir").Trim();
        if (logger.Length == 0)
        {
            logger = "weir";
        }

        var message = OrText(dict.Get("message"), string.Empty).Trim();
        if (timestamp.Length == 0 || message.Length == 0)
        {
            return null;
        }

        var source = dict.Get("source");
        return new ParsedLogEntry(
            timestamp,
            level,
            ComponentLabel(logger, source),
            message,
            CleanOptional(dict.Get("detail")),
            CleanOptional(dict.Get("traceback")),
            CleanOptional(source),
            logger,
            CleanOptional(dict.Get("correlation_id")),
            CleanOptional(dict.Get("job_id")));
    }

    /// <summary>The entry as the Logs API returns it, or <see langword="null"/> when the timestamp does not parse.</summary>
    public static WireObject? ToOut(ParsedLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!Timestamp.TryFromIsoFormat(entry.Timestamp.Replace("Z", "+00:00", StringComparison.Ordinal), out var parsed))
        {
            return null;
        }

        return new WireObject()
            .Set("timestamp", parsed.ToWireText())
            .Set("level", entry.Level)
            .Set("component", entry.Component)
            .Set("message", entry.Message)
            .Set("detail", entry.Detail)
            .Set("traceback", entry.Traceback)
            .Set("source", entry.Source)
            .Set("logger", entry.Logger)
            .Set("correlation_id", entry.CorrelationId)
            .Set("job_id", entry.JobId);
    }

    private static string OrText(WireValue? value, string fallback) => value is null || !value.IsTruthy ? fallback : WireConvert.Str(value);

    private static string ComponentLabel(string logger, WireValue? source)
    {
        var sourceText = source is null || !source.IsTruthy ? string.Empty : WireConvert.Str(source);
        var haystack = $"{logger} {sourceText}".ToLowerInvariant();
        if (haystack.Contains("weir.processing", StringComparison.Ordinal))
        {
            return "Processing";
        }

        if (haystack.Contains("platform.auth", StringComparison.Ordinal))
        {
            return "Authentication";
        }

        return haystack.Contains("platform.activity", StringComparison.Ordinal) ? "Activity" : "System";
    }

    private static string? CleanOptional(WireValue? value)
    {
        if (value is null or WireNull)
        {
            return null;
        }

        var text = WireConvert.Str(value).Trim();
        return text.Length == 0 ? null : text;
    }

    private static bool SkipLowValueNoise(ParsedLogEntry entry) =>
        entry.Level is not ("ERROR" or "WARNING" or "CRITICAL") && !entry.Logger.StartsWith("weir", StringComparison.Ordinal);
}
