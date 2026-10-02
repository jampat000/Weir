using System.Numerics;
using Weir.Api.Http;
using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Core.Logs;
using Weir.Core.Validation;
using Weir.Infrastructure.SystemLog;

namespace Weir.Api.Endpoints;

/// <summary>What a request for System › Logs asks for: the filters, where to start and how many rows.</summary>
internal sealed record SystemLogQuery(SystemLogFilter Filter, SystemLogPosition? After, int Limit)
{
    private const int TextMaxLength = 200;
    private const int EventTypeMaxLength = 64;
    private const char ListSeparator = ',';

    /// <summary>The query string as a filter. Every problem is reported at once, as the other lists do.</summary>
    public static SystemLogQuery Read(ApiRequest request, ValidationIssues issues)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(issues);
        var sources = List(request, "source", SystemLogSources.All.Select(SystemLogSources.NameOf).ToList(), issues);
        var filter = new SystemLogFilter
        {
            Sources = [.. sources.Select(name => SystemLogSources.TryParse(name, out var source) ? source : default)],
            Levels = List(request, "level", SystemLogLevels.All, issues),
            Categories = List(request, "category", SystemLogCategories.All, issues),
            JobStatuses = List(request, "status", [.. SystemLogRules.JobStatusLabels.Keys], issues),
            WorkflowId = Integer(request, "workflow", issues),
            JobId = Integer(request, "job", issues),
            Text = Text(request, "q", TextMaxLength, issues),
            EventType = Text(request, "event_type", EventTypeMaxLength, issues),
            Result = Literal(request, "result", [.. ActivityClassifier.Results], issues),
            Trigger = Literal(request, "trigger", [.. ActivityClassifier.Triggers], issues),
            HasException = Flag(request, "has_exception", issues),
            From = When(request, "from", issues),
            To = When(request, "to", issues),
        };
        var limit = ReadLimit(request, issues);
        var after = Cursor(request, issues);
        return new SystemLogQuery(filter, after, limit);
    }

    /// <summary>The values of a parameter that may be repeated, or written once with commas between them, each one of <paramref name="allowed"/>.</summary>
    private static List<string> List(ApiRequest request, string name, IReadOnlyList<string> allowed, ValidationIssues issues)
    {
        var values = new List<string>();
        foreach (var raw in request.Context.Request.Query[name])
        {
            foreach (var part in (raw ?? string.Empty).Split(ListSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (FieldRules.TryLiteral(new WireString(part), ["query", name], allowed, issues, out var value) && !values.Contains(value))
                {
                    values.Add(value);
                }
            }
        }

        return values;
    }

    private static string? Literal(ApiRequest request, string name, IReadOnlyList<string> allowed, ValidationIssues issues) =>
        request.Query(name) is { Length: > 0 } raw && FieldRules.TryLiteral(new WireString(raw), ["query", name], allowed, issues, out var value) ? value : null;

    private static string? Text(ApiRequest request, string name, int maxLength, ValidationIssues issues) =>
        request.Query(name)?.Trim() is { Length: > 0 } raw && FieldRules.TryStr(new WireString(raw), ["query", name], 1, maxLength, issues, out var value) ? value : null;

    private static long? Integer(ApiRequest request, string name, ValidationIssues issues) =>
        request.Query(name) is { Length: > 0 } raw && FieldRules.TryInt(new WireString(raw), ["query", name], 1, null, issues, out var value)
            ? (long)BigInteger.Min(value, long.MaxValue)
            : null;

    private static bool? Flag(ApiRequest request, string name, ValidationIssues issues) =>
        request.Query(name) is { Length: > 0 } raw && FieldRules.TryBool(new WireString(raw), ["query", name], issues, out var value) ? value : null;

    private static DateTimeOffset? When(ApiRequest request, string name, ValidationIssues issues)
    {
        if (request.Query(name) is not { Length: > 0 } raw || !FieldRules.TryDateTime(new WireString(raw), ["query", name], issues, out var value) || value is not { } moment)
        {
            return null;
        }

        return new DateTimeOffset(moment.AsUtc, TimeSpan.Zero);
    }

    private static int ReadLimit(ApiRequest request, ValidationIssues issues) =>
        request.Query("limit") is { Length: > 0 } raw && FieldRules.TryInt(new WireString(raw), ["query", "limit"], 1, SystemLogReader.MaxLimit, issues, out var value)
            ? (int)value
            : SystemLogReader.DefaultLimit;

    private static SystemLogPosition? Cursor(ApiRequest request, ValidationIssues issues)
    {
        if (request.Query("cursor") is not { Length: > 0 } raw)
        {
            return null;
        }

        if (SystemLogCursor.TryDecode(raw, out var position))
        {
            return position;
        }

        issues.Add(new ValidationIssue("value_error", ["query", "cursor"], "Value error, that is not a position this log gave out", new WireString(raw)));
        return null;
    }
}
