using Weir.Api.Endpoints;
using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Core.Logs;

namespace Weir.Api.Tests.Platform;

/// <summary>A log export written a page at a time reads exactly as the same export written whole.</summary>
public sealed class SystemLogExportWriterTests
{
    private static readonly IReadOnlyDictionary<long, string> NoNames = new Dictionary<long, string>();

    private static SystemLogRow Row(int key) => new(
        SystemLogSource.Event,
        key,
        new DateTimeOffset(2026, 5, 9, 10, 0, key, TimeSpan.Zero),
        "success",
        "scans",
        null,
        $"Scan {key}, with a comma",
        null,
        new WireObject().Set("detail", $"line one\nline two {key}"));

    private static string Written(SystemLogExportWriter writer, params int[][] pages) =>
        writer.Start() + string.Concat(pages.Select(page => writer.Rows([.. page.Select(Row)], NoNames))) + writer.End();

    [Fact]
    public void A_json_export_in_pages_is_the_same_text_as_the_whole_list_written_at_once()
    {
        var whole = WireJsonWriter.Dumps(
            new WireArray(Enumerable.Range(1, 5).Select(key => (WireValue)SystemLogWire.Row(Row(key), NoNames))),
            ActivityHistory.ExportJsonFormat);

        var inPages = Written(new SystemLogExportWriter(json: true), [1, 2], [], [3, 4, 5]);

        Assert.Equal(whole, inPages);
    }

    [Fact]
    public void A_csv_export_in_pages_is_the_same_text_as_the_whole_list_written_at_once()
    {
        var whole = Written(new SystemLogExportWriter(json: false), [1, 2, 3, 4, 5]);

        var inPages = Written(new SystemLogExportWriter(json: false), [1, 2], [3], [4, 5]);

        Assert.Equal(whole, inPages);
        Assert.Equal(6, inPages.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void An_export_with_no_rows_is_an_empty_json_list_or_a_csv_header()
    {
        Assert.Equal("[]", Written(new SystemLogExportWriter(json: true), []));
        Assert.Equal("time,source,level,category,workflow,title,detail\r\n", Written(new SystemLogExportWriter(json: false), []));
    }
}
