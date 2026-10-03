using System.Globalization;
using System.Text;
using Weir.Core.Json;

namespace Weir.Core.Metrics;

/// <summary>
/// In-memory runtime metrics for the dashboard and the Prometheus scrape.
/// </summary>
public sealed class RuntimeMetricsStore
{
    private static readonly string[] LogLevels = ["DEBUG", "INFO", "WARNING", "ERROR", "CRITICAL"];
    private static readonly string[] JobEvents = ["started", "completed", "failed"];
    private static readonly string[] StatusBuckets = ["2xx", "3xx", "4xx", "5xx"];

    private const int MaxTimingSamples = 20_000;
    private const int MaxServerErrorsKept = 10_000;
    private const double SlowRequestFraction = 0.95;
    private const double MedianFraction = 0.5;

    /// <summary>How far back the request timings reach: what "how fast Weir answers" means.</summary>
    public static readonly TimeSpan TimingWindow = TimeSpan.FromMinutes(15);

    /// <summary>How long a server error is remembered, enough for any "today" in any time zone.</summary>
    private static readonly TimeSpan ServerErrorMemory = TimeSpan.FromDays(2);

    private readonly Lock _lock = new();
    private readonly TimeProvider _time;
    private readonly DateTimeOffset _startedAt;
    private readonly Dictionary<string, long> _statusCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Count, double TotalMs)> _routes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _logCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Module, string Event), long> _moduleJobCounts = [];
    private readonly Dictionary<string, long> _queueDepths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _savings = new(StringComparer.Ordinal);
    private readonly Queue<(DateTimeOffset At, double Ms)> _timings = new();
    private readonly Queue<DateTimeOffset> _serverErrors = new();
    private long _httpTotal;
    private double _httpTotalMs;

    public RuntimeMetricsStore(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        _startedAt = time.GetUtcNow();
    }

    /// <summary>One answered request: counted, and its time kept for <see cref="GetRequestFigures"/>.</summary>
    public void RecordRequest(string method, string route, int statusCode, double durationMs)
    {
        lock (_lock)
        {
            Count(method, route, statusCode, durationMs);
            var now = _time.GetUtcNow();
            _timings.Enqueue((now, Math.Max(durationMs, 0.0)));
            DiscardOldTimings(now);
            if (statusCode >= 500)
            {
                _serverErrors.Enqueue(now);
                DiscardOldServerErrors(now);
            }
        }
    }

    /// <summary>
    /// A request that held its connection open for as long as a browser watched (an event stream): counted, but its time says
    /// nothing about how fast Weir answers, so <see cref="GetRequestFigures"/> leaves it out.
    /// </summary>
    public void RecordStreamRequest(string method, string route, int statusCode, double durationMs)
    {
        lock (_lock)
        {
            Count(method, route, statusCode, durationMs);
        }
    }

    /// <summary>
    /// How fast requests were answered over the last <see cref="TimingWindow"/> (zero when there were none), and how many
    /// answered with a server error since <paramref name="errorsSince"/> (remembered for two days).
    /// </summary>
    public RequestFigures GetRequestFigures(DateTimeOffset errorsSince)
    {
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            DiscardOldTimings(now);
            DiscardOldServerErrors(now);
            var sorted = _timings.Select(sample => sample.Ms).Order().ToArray();
            return new RequestFigures(
                Percentile(sorted, MedianFraction),
                Percentile(sorted, SlowRequestFraction),
                _serverErrors.Count(at => at >= errorsSince));
        }
    }

    private void Count(string method, string route, int statusCode, double durationMs)
    {
        ArgumentNullException.ThrowIfNull(method);
        var bucket = $"{statusCode / 100}xx";
        var label = $"{method.ToUpperInvariant()} {route}";
        _httpTotal++;
        _httpTotalMs += Math.Max(durationMs, 0.0);
        _statusCounts[bucket] = _statusCounts.GetValueOrDefault(bucket) + 1;
        var (count, total) = _routes.GetValueOrDefault(label);
        _routes[label] = (count + 1, total + Math.Max(durationMs, 0.0));
    }

    private void DiscardOldTimings(DateTimeOffset now)
    {
        while (_timings.Count > 0 && (_timings.Count > MaxTimingSamples || now - _timings.Peek().At > TimingWindow))
        {
            _timings.Dequeue();
        }
    }

    private void DiscardOldServerErrors(DateTimeOffset now)
    {
        while (_serverErrors.Count > 0 && (_serverErrors.Count > MaxServerErrorsKept || now - _serverErrors.Peek() > ServerErrorMemory))
        {
            _serverErrors.Dequeue();
        }
    }

    /// <summary>The value at <paramref name="fraction"/> of the way through <paramref name="sorted"/> (nearest rank); zero when empty.</summary>
    private static double Percentile(double[] sorted, double fraction) =>
        sorted.Length == 0 ? 0.0 : sorted[Math.Clamp((int)Math.Ceiling(fraction * sorted.Length) - 1, 0, sorted.Length - 1)];

    public void RecordLog(string level)
    {
        ArgumentNullException.ThrowIfNull(level);
        lock (_lock)
        {
            var key = level.ToUpperInvariant();
            _logCounts[key] = _logCounts.GetValueOrDefault(key) + 1;
        }
    }

    public void RecordModuleJobEvent(string module, string jobEvent)
    {
        if (string.IsNullOrEmpty(module) || !JobEvents.Contains(jobEvent, StringComparer.Ordinal))
        {
            return;
        }

        lock (_lock)
        {
            _moduleJobCounts[(module, jobEvent)] = _moduleJobCounts.GetValueOrDefault((module, jobEvent)) + 1;
        }
    }

    public void SetModuleQueueDepth(string module, long depth)
    {
        if (string.IsNullOrEmpty(module))
        {
            return;
        }

        lock (_lock)
        {
            _queueDepths[module] = Math.Max(0, depth);
        }
    }

    public void RecordModuleSavings(string module, long bytesSaved)
    {
        if (string.IsNullOrEmpty(module) || bytesSaved <= 0)
        {
            return;
        }

        lock (_lock)
        {
            _savings[module] = _savings.GetValueOrDefault(module) + bytesSaved;
        }
    }

    /// <summary>How fast requests were answered recently and how many failed on the server.</summary>
    public sealed record RequestFigures(double MedianMs, double P95Ms, long ServerErrors);

    public sealed record RouteSummary(string Route, long RequestCount, double AverageResponseMs);

    public sealed record Summary(
        double UptimeSeconds,
        long TotalRequests,
        double AverageResponseMs,
        long ErrorLogCount,
        IReadOnlyList<KeyValuePair<string, long>> StatusCounts,
        IReadOnlyList<RouteSummary> BusiestRoutes,
        IReadOnlyList<KeyValuePair<string, IReadOnlyList<KeyValuePair<string, long>>>> ModuleJobCounts,
        IReadOnlyList<KeyValuePair<string, long>> ModuleQueueDepths,
        IReadOnlyList<KeyValuePair<string, long>> ModuleSavingsBytes,
        IReadOnlyDictionary<string, long> LogCounts);

    public Summary GetSummary()
    {
        lock (_lock)
        {
            var uptime = Math.Max((_time.GetUtcNow() - _startedAt).TotalSeconds, 0.0);
            var average = _httpTotal > 0 ? _httpTotalMs / _httpTotal : 0.0;
            var busiest = _routes
                .OrderBy(pair => -pair.Value.Count)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Take(5)
                .Select(pair => new RouteSummary(pair.Key, pair.Value.Count, pair.Value.Count > 0 ? pair.Value.TotalMs / pair.Value.Count : 0.0))
                .ToList();
            var jobCounts = new List<KeyValuePair<string, IReadOnlyList<KeyValuePair<string, long>>>>();
            foreach (var group in _moduleJobCounts
                .OrderBy(pair => pair.Key.Module, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Event, StringComparer.Ordinal)
                .GroupBy(pair => pair.Key.Module))
            {
                var buckets = JobEvents.ToDictionary(e => e, _ => 0L, StringComparer.Ordinal);
                foreach (var pair in group)
                {
                    buckets[pair.Key.Event] = pair.Value;
                }

                jobCounts.Add(new(group.Key, [.. JobEvents.Select(e => new KeyValuePair<string, long>(e, buckets[e]))]));
            }

            return new Summary(
                uptime,
                _httpTotal,
                average,
                _logCounts.GetValueOrDefault("ERROR") + _logCounts.GetValueOrDefault("CRITICAL"),
                [.. StatusBuckets.Select(b => new KeyValuePair<string, long>(b, _statusCounts.GetValueOrDefault(b)))],
                busiest,
                jobCounts,
                [.. _queueDepths],
                [.. _savings],
                new Dictionary<string, long>(_logCounts, StringComparer.Ordinal));
        }
    }

    /// <summary>The <c>GET /api/v1/suite/metrics</c> body.</summary>
    public WireObject SuiteMetricsOut()
    {
        var summary = GetSummary();
        var statusCounts = new WireObject();
        foreach (var (bucket, count) in summary.StatusCounts)
        {
            statusCounts.Set(bucket, count);
        }

        return new WireObject()
            .Set("uptime_seconds", summary.UptimeSeconds)
            .Set("total_requests", summary.TotalRequests)
            .Set("average_response_ms", summary.AverageResponseMs)
            .Set("error_log_count", summary.ErrorLogCount)
            .Set("status_counts", statusCounts)
            .Set("busiest_routes", new WireArray(summary.BusiestRoutes.Select(route => (WireValue)new WireObject()
                .Set("route", route.Route)
                .Set("request_count", route.RequestCount)
                .Set("average_response_ms", route.AverageResponseMs))));
    }

    /// <summary>The Prometheus text exposition.</summary>
    public string RenderPrometheus()
    {
        var summary = GetSummary();
        var lines = new List<string>
        {
            "# HELP weir_process_uptime_seconds Seconds since the current Weir process started.",
            "# TYPE weir_process_uptime_seconds gauge",
            "weir_process_uptime_seconds " + F3(summary.UptimeSeconds),
            "# HELP weir_http_requests_total Total HTTP requests handled by Weir.",
            "# TYPE weir_http_requests_total counter",
            "weir_http_requests_total " + summary.TotalRequests.ToString(CultureInfo.InvariantCulture),
            "# HELP weir_http_average_response_ms Average HTTP response time in milliseconds.",
            "# TYPE weir_http_average_response_ms gauge",
            "weir_http_average_response_ms " + F3(summary.AverageResponseMs),
            "# HELP weir_log_records_total Total log records by level.",
            "# TYPE weir_log_records_total counter",
        };
        foreach (var level in LogLevels)
        {
            lines.Add($"weir_log_records_total{{level=\"{level.ToLowerInvariant()}\"}} {summary.LogCounts.GetValueOrDefault(level).ToString(CultureInfo.InvariantCulture)}");
        }

        foreach (var (bucket, count) in summary.StatusCounts)
        {
            lines.Add($"weir_http_status_total{{status=\"{bucket}\"}} {count.ToString(CultureInfo.InvariantCulture)}");
        }

        foreach (var (module, counts) in summary.ModuleJobCounts)
        {
            foreach (var (jobEvent, count) in counts)
            {
                lines.Add($"weir_module_jobs_total{{module=\"{module}\",event=\"{jobEvent}\"}} {count.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        foreach (var (module, depth) in summary.ModuleQueueDepths)
        {
            lines.Add($"weir_module_queue_depth{{module=\"{module}\"}} {depth.ToString(CultureInfo.InvariantCulture)}");
        }

        if (summary.ModuleSavingsBytes.Count > 0)
        {
            lines.Add("# HELP weir_module_savings_bytes_total Bytes saved by each module (remux).");
            lines.Add("# TYPE weir_module_savings_bytes_total counter");
            foreach (var (module, total) in summary.ModuleSavingsBytes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                lines.Add($"weir_module_savings_bytes_total{{module=\"{module}\"}} {total.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    private static string F3(double value) => value.ToString("F3", CultureInfo.InvariantCulture);
}
