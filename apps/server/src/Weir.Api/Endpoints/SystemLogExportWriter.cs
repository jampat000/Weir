using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Core.Logs;

namespace Weir.Api.Endpoints;

/// <summary>
/// The text of a log export, a page of rows at a time: the same bytes a whole export written at once would have, so an export
/// of tens of thousands of rows never has to be held in memory.
/// </summary>
internal sealed class SystemLogExportWriter
{
    private const string JsonOpen = "[";
    private const string JsonClose = "]";
    private const string JsonEmpty = "[]";
    private const string JsonItemSeparator = ",";
    private const string JsonNewLine = "\n";
    private const int JsonItemLevel = 1;

    private static readonly string JsonItemIndent = new(' ', (ActivityHistory.ExportJsonFormat.Indent ?? 0) * JsonItemLevel);

    private readonly bool _json;
    private bool _anyRow;

    public SystemLogExportWriter(bool json) => _json = json;

    public string ContentType => _json ? "application/json" : "text/csv; charset=utf-8";

    public string Extension => _json ? "json" : "csv";

    /// <summary>What comes before the first row.</summary>
    public string Start() => _json ? string.Empty : SystemLogWire.CsvHeader();

    /// <summary>The text for one page of rows, which follows the text for every page before it.</summary>
    public string Rows(IReadOnlyList<SystemLogRow> rows, IReadOnlyDictionary<long, string> workflowNames)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (!_json)
        {
            return SystemLogWire.CsvLines(rows, workflowNames);
        }

        var items = rows.Select(row => JsonNewLine + JsonItemIndent + WireJsonWriter.DumpsAtLevel(SystemLogWire.Row(row, workflowNames), ActivityHistory.ExportJsonFormat, JsonItemLevel));
        var text = string.Join(JsonItemSeparator, items);
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var opening = _anyRow ? JsonItemSeparator : JsonOpen;
        _anyRow = true;
        return opening + text;
    }

    /// <summary>What comes after the last row.</summary>
    public string End() => !_json ? string.Empty : _anyRow ? JsonNewLine + JsonClose : JsonEmpty;
}
