using System.Text;
using Microsoft.Data.Sqlite;
using Weir.Core.Paging;
using Weir.Core.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>
/// How a list of <c>files</c> is ordered and paged. A file's place in a list is its key: the values of the sort's own parts,
/// then its id, which no other file shares, so a page can start right after any file. Every part runs the way the list does.
/// </summary>
public static class ProcessingFileOrdering
{
    private const string IdColumn = "id";

    private static readonly string MeaningRankSql = BuildMeaningRankSql();

    /// <summary>The key a cursor holds for <paramref name="sort"/>: a part for each of its values.</summary>
    public static IReadOnlyList<KeysetPart> Parts(ProcessingFileSort sort, SortDirection direction) =>
        [.. Keys(sort, direction).Select(key => key.Part)];

    public static string EncodeCursor(ProcessingFileSort sort, SortDirection direction, IReadOnlyList<object?> key) =>
        KeysetCursor.Encode(ProcessingFileSorts.NameOf(sort), direction, key);

    /// <summary>The key a cursor stands for, or false when it is not one this list gave out under <paramref name="sort"/> in <paramref name="direction"/>.</summary>
    public static bool TryDecodeCursor(string? cursor, ProcessingFileSort sort, SortDirection direction, out IReadOnlyList<object?> after) =>
        KeysetCursor.TryDecode(cursor, ProcessingFileSorts.NameOf(sort), direction, Parts(sort, direction), out after);

    internal static IReadOnlyList<KeysetKey> Keys(ProcessingFileSort sort, SortDirection direction)
    {
        KeysetKey Key(string expression, KeysetValueKind kind, bool canBeNull = false) =>
            new(expression, new KeysetPart(kind, direction), canBeNull);

        return sort switch
        {
            ProcessingFileSort.File => [Key("relative_path COLLATE NOCASE", KeysetValueKind.TextIgnoringCase), Key(IdColumn, KeysetValueKind.Number)],
            ProcessingFileSort.Status =>
            [
                Key(MeaningRankSql, KeysetValueKind.Number),
                Key("status", KeysetValueKind.Text),
                Key(IdColumn, KeysetValueKind.Number),
            ],
            ProcessingFileSort.When => [Key("updated_at", KeysetValueKind.Text), Key(IdColumn, KeysetValueKind.Number)],
            _ => [Key("last_seen_at", KeysetValueKind.Text, canBeNull: true), Key(IdColumn, KeysetValueKind.Number)],
        };
    }

    /// <summary>The key of the row the reader is on, from the columns that follow the file's own, starting at <paramref name="firstOrdinal"/>.</summary>
    internal static IReadOnlyList<object?> ReadKey(SqliteDataReader reader, int firstOrdinal, IReadOnlyList<KeysetKey> keys)
    {
        var values = new List<object?>(keys.Count);
        for (var index = 0; index < keys.Count; index++)
        {
            var ordinal = firstOrdinal + index;
            values.Add(reader.IsDBNull(ordinal)
                ? null
                : keys[index].Part.Kind == KeysetValueKind.Number ? reader.GetInt64(ordinal) : reader.GetString(ordinal));
        }

        return values;
    }

    private static string BuildMeaningRankSql()
    {
        var builder = new StringBuilder("CASE status");
        foreach (var (status, meaning) in ProcessingFileMeanings.OfStatus)
        {
            builder.Append(" WHEN '").Append(status).Append("' THEN ").Append((int)meaning);
        }

        return builder.Append(" ELSE ").Append(ProcessingFileMeanings.RankOf(string.Empty)).Append(" END").ToString();
    }
}
