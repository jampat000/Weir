using System.Globalization;
using Microsoft.Data.Sqlite;
using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Core.Time;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>SQLite access for <c>file_logs</c>.</summary>
public sealed class FileLogStore
{
    public const int MaxDetailChars = 200_000;

    private const string Columns = "id, file_id, library_id, relative_path, library_name, outcome, title, detail_json, recorded_at";

    /// <summary>Every retained pass over this file, newest first, matched by path.</summary>
    public Task<List<ProcessingFileLogRecord>> LogsForFileAsync(UnitOfWork uow, string relativePath, int limit) =>
        uow.QueryAsync(
            $"SELECT {Columns} FROM file_logs WHERE relative_path = @path ORDER BY recorded_at DESC, id DESC LIMIT {Math.Max(1, Math.Min(limit, 500))}",
            Read, ("@path", relativePath));

    /// <summary>Parse a stored detail payload as a <see cref="WireObject"/>, never raising.</summary>
    public WireObject ParseDetail(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new WireObject();
        }

        try
        {
            return WireJsonParser.Parse(raw) switch
            {
                WireObject dict => dict,
                WireValue other => new WireObject().Set("detail", other),
            };
        }
        catch (WireJsonDecodeException)
        {
            return new WireObject().Set("unparsed_detail", raw);
        }
    }

    /// <summary>A plain-text rendering for attaching to a bug report.</summary>
    public string RenderLogText(IReadOnlyList<ProcessingFileLogRecord> rows)
    {
        if (rows.Count == 0)
        {
            return "Weir has no retained processing records for this file.\n";
        }

        var lines = new List<string>();
        var first = rows[0];
        lines.Add($"Weir — processing record for {first.RelativePath}");
        if (first.LibraryName.Length > 0)
        {
            lines.Add($"Library: {first.LibraryName}");
        }

        lines.Add($"Records retained: {rows.Count} (newest first)");
        lines.Add(string.Empty);

        foreach (var row in rows)
        {
            lines.Add(new string('=', 78));
            lines.Add($"{row.RecordedAt.ToWireText()}  —  {(row.Outcome.Length > 0 ? row.Outcome : "no outcome recorded")}");
            if (row.Title.Length > 0)
            {
                lines.Add(row.Title);
            }

            lines.Add(new string('-', 78));
            var detail = ParseDetail(row.DetailJson);
            foreach (var key in detail.Keys.Order(StringComparer.Ordinal))
            {
                var value = detail[key];
                var rendered = value is WireObject or WireArray ? WireJsonWriter.Dumps(value, WireJsonFormat.Compact) : PlainValue(value);
                lines.Add($"{key}: {rendered}");
            }

            lines.Add(string.Empty);
        }

        return string.Join('\n', lines) + "\n";
    }

    private static string PlainValue(WireValue value) => value switch
    {
        WireString s => s.Value,
        WireInteger i => i.Value.ToString(CultureInfo.InvariantCulture),
        WireNumber f => f.Value.ToString(CultureInfo.InvariantCulture),
        WireBool b => b.Value ? "True" : "False",
        WireNull => "None",
        _ => string.Empty,
    };

    /// <summary>Deletes records older than the retention window, a batch per transaction. 0 keeps everything.</summary>
    public Task<int> PruneAsync(SqliteDatabase database, long retentionDays, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (retentionDays <= 0)
        {
            return Task.FromResult(0);
        }

        var cutoff = now.AddDays(-retentionDays);
        return BatchedDeletes.DeleteAsync(
            database, "file_logs", "recorded_at < @cutoff", [("@cutoff", SqliteValues.ToSqlite(Timestamp.FromUtc(cutoff.UtcDateTime)))], cancellationToken);
    }

    private static ProcessingFileLogRecord Read(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        FileId = reader.IsDBNull(1) ? null : reader.GetInt64(1),
        LibraryId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
        RelativePath = SqliteValues.GetString(reader, 3),
        LibraryName = SqliteValues.GetString(reader, 4),
        Outcome = SqliteValues.GetString(reader, 5),
        Title = SqliteValues.GetString(reader, 6),
        DetailJson = SqliteValues.GetString(reader, 7),
        RecordedAt = SqliteValues.GetDateTime(reader, 8),
    };
}
