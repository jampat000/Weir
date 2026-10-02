using System.Globalization;
using System.Text;
using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Core.Logs;
using Weir.Core.Time;

namespace Weir.Api.Endpoints;

/// <summary>System › Logs as the API returns it, and as the export writes it.</summary>
internal static class SystemLogWire
{
    /// <summary>The export columns, in order.</summary>
    private static readonly string[] ExportColumns = ["time", "source", "level", "category", "workflow", "title", "detail"];

    /// <summary>The page: its rows, a cursor for the next page when there is one, and what each filter choice would show.</summary>
    public static WireObject Page(SystemLogPage page, IReadOnlyDictionary<long, string> workflowNames)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(workflowNames);
        return new WireObject()
            .Set("items", new WireArray(page.Rows.Select(row => (WireValue)Row(row, workflowNames))))
            .Set("next_cursor", page.NextCursor)
            .Set("total", page.Total)
            .Set("counts", new WireObject()
                .Set("source", Tally(page.Counts.BySource))
                .Set("level", Tally(page.Counts.ByLevel))
                .Set("category", Tally(page.Counts.ByCategory)));
    }

    /// <summary>One row: what every source shares, and its own record under the name of its source.</summary>
    public static WireObject Row(SystemLogRow row, IReadOnlyDictionary<long, string> workflowNames)
    {
        ArgumentNullException.ThrowIfNull(row);
        var wire = new WireObject()
            .Set("id", row.Id)
            .Set("source", SystemLogSources.NameOf(row.Source))
            .Set("at", Timestamp.FromUtc(row.At.UtcDateTime).ToWireText())
            .Set("level", row.Level)
            .Set("category", row.Category)
            .Set("workflow", Workflow(row.WorkflowId, workflowNames))
            .Set("title", row.Title)
            .Set("detail", row.Detail);
        foreach (var source in SystemLogSources.All)
        {
            wire.Set(SystemLogSources.NameOf(source), source == row.Source ? row.Record : WireValue.Null);
        }

        return wire;
    }

    public static string Json(IReadOnlyList<SystemLogRow> rows, IReadOnlyDictionary<long, string> workflowNames) =>
        WireJsonWriter.Dumps(new WireArray(rows.Select(row => (WireValue)Row(row, workflowNames))), ActivityHistory.ExportJsonFormat);

    /// <summary>A header and a line for each row; a line of a row with no detail leaves it empty.</summary>
    public static string Csv(IReadOnlyList<SystemLogRow> rows, IReadOnlyDictionary<long, string> workflowNames)
    {
        var builder = new StringBuilder();
        ActivityHistory.AppendCsvLine(builder, ExportColumns);
        foreach (var row in rows)
        {
            ActivityHistory.AppendCsvLine(
                builder,
                [
                    Timestamp.FromUtc(row.At.UtcDateTime).IsoFormat(),
                    SystemLogSources.NameOf(row.Source),
                    row.Level,
                    row.Category,
                    row.WorkflowId is { } id ? workflowNames.GetValueOrDefault(id) ?? id.ToString(CultureInfo.InvariantCulture) : string.Empty,
                    row.Title,
                    ExportDetail(row),
                ]);
        }

        return builder.ToString();
    }

    /// <summary><c>weir-log-{stamp}.{extension}</c>, the stamp being local time as <c>yyyyMMdd-HHmmss</c>.</summary>
    public static string ExportFileName(DateTimeOffset localNow, string extension) =>
        string.Create(CultureInfo.InvariantCulture, $"weir-log-{localNow:yyyyMMdd-HHmmss}.{extension}");

    private static WireValue Workflow(long? id, IReadOnlyDictionary<long, string> names) =>
        id is { } workflowId
            ? new WireObject().Set("id", workflowId).Set("name", names.GetValueOrDefault(workflowId))
            : WireValue.Null;

    private static WireObject Tally(IReadOnlyDictionary<string, long> counts)
    {
        var wire = new WireObject();
        foreach (var (key, count) in counts)
        {
            wire.Set(key, count);
        }

        return wire;
    }

    /// <summary>
    /// The line a spreadsheet gets under Detail: an event's own record of what happened, a job's last error, or a server line's
    /// exception.
    /// </summary>
    private static string ExportDetail(SystemLogRow row)
    {
        var field = row.Source switch
        {
            SystemLogSource.Event => "detail",
            SystemLogSource.Job => "last_error",
            _ => "traceback",
        };
        var own = row.Record.Get(field) is { } value and not WireNull ? WireConvert.Str(value) : null;
        return own ?? row.Detail ?? string.Empty;
    }
}
