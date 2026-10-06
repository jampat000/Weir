using System.Globalization;
using System.Text.Json.Nodes;

namespace Weir.Contract.Tests.Libraries;

/// <summary>What the Files list should show for each sort, worked out from the rows themselves rather than from the server's rules.</summary>
public static class FilesSortModel
{
    public const int Done = 0;
    public const int ToDo = 1;

    // What each status means, in the order a list sorted by status shows them: done, to do, doing, needs a look, broken, idle.
    public static readonly Dictionary<string, int> MeaningRank = new()
    {
        ["processed"] = Done,
        ["unprocessed"] = ToDo,
        ["out_of_schedule"] = ToDo,
        ["processing"] = 2,
        ["on_hold"] = 3,
        ["blocked_upstream"] = ToDo,
        ["passed_through"] = 3,
        ["rejected"] = 3,
        ["processing_failed"] = 4,
        ["skipped"] = 5,
        ["disabled"] = 5,
        ["cancelled"] = 5,
    };

    public static readonly string[] StatusesByMeaning =
    [
        "processed",
        "blocked_upstream",
        "out_of_schedule",
        "unprocessed",
        "processing",
        "on_hold",
        "passed_through",
        "rejected",
        "processing_failed",
        "cancelled",
        "disabled",
        "skipped",
    ];

    public static long Id(JsonObject row) => (long)row["id"]!;

    public static string StatusOf(JsonObject row) => (string)row["status"]!;

    public static DateTimeOffset? Time(JsonNode? text) =>
        (string?)text is { } value ? DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal) : null;

    /// <summary>Whether the file's cleaned copy still waits for a media manager: nobody has answered and Weir has not settled it.</summary>
    public static bool AwaitsImport(JsonObject row)
    {
        if (row["handback"] is not JsonObject handback)
        {
            return false;
        }

        var answered = !string.IsNullOrEmpty((string?)handback["outcome"]);
        var settled = !string.IsNullOrEmpty((string?)handback["settled_at"]) && !string.IsNullOrEmpty((string?)handback["release_note"]);
        return !answered && !settled;
    }

    /// <summary>Where the file stands when sorted by status: a copy waiting for a media manager is to do, whatever its status.</summary>
    public static int Rank(JsonObject row) => AwaitsImport(row) ? ToDo : MeaningRank[StatusOf(row)];

    /// <summary>The files as the order should read, worked out from what each row shows rather than from the server's rules.</summary>
    public static List<long> Expected(IEnumerable<JsonObject> rows, string? sort, string direction)
    {
        var ordered = sort switch
        {
            "file" => rows.OrderBy(row => ((string)row["relative_path"]!).ToLowerInvariant(), StringComparer.Ordinal).ThenBy(Id),
            "status" => rows.OrderBy(Rank).ThenBy(StatusOf, StringComparer.Ordinal).ThenBy(Id),
            "when" => rows.OrderBy(row => Time(row["updated_at"])).ThenBy(Id),
            // A file no scan has seen sorts before every file one has seen.
            _ => rows.OrderBy(row => Time(row["last_seen_at"]) is not null)
                .ThenBy(row => Time(row["last_seen_at"]) ?? DateTimeOffset.MinValue)
                .ThenBy(Id),
        };
        var ids = ordered.Select(Id).ToList();
        if (direction == "desc")
        {
            ids.Reverse();
        }

        return ids;
    }
}
