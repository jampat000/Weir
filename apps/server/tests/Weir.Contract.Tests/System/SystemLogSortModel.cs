using System.Globalization;
using System.Text.Json.Nodes;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>
/// The order the log should read in for each sort, worked out from what each row shows rather than from the
/// server's rules.
/// </summary>
internal static class SystemLogSortModel
{
    public static readonly string[] Sorts = ["time", "level", "source", "category", "workflow"];
    public static readonly string[] Directions = ["asc", "desc"];

    public static readonly IReadOnlyDictionary<string, int> LevelRank = new Dictionary<string, int>
    {
        ["error"] = 0,
        ["warning"] = 1,
        ["info"] = 2,
        ["success"] = 3,
    };

    private static readonly Dictionary<string, int> SourceRank = new() { ["server"] = 0, ["job"] = 1, ["event"] = 2 };

    public static DateTimeOffset When(JsonNode item) => DateTimeOffset.Parse((string)item["at"]!, CultureInfo.InvariantCulture);

    /// <summary>When it happened, its source and its number in the source: what every order ends in, and no two rows share.</summary>
    public static (DateTimeOffset When, int Source, int Number) Place(JsonNode item)
    {
        var parts = ((string)item["id"]!).Split(':');
        return (When(item), SourceRank[parts[0]], int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    public static bool HasWorkflow(JsonNode item) => item["workflow"] is JsonObject;

    public static List<string> Expected(IReadOnlyList<JsonNode> items, string sort, string direction)
    {
        var descending = direction == "desc";
        if (sort == "workflow")
        {
            var named = items.Where(item => HasWorkflow(item) && SystemPartBJson.IsTruthy(item["workflow"]!["name"])).ToList();
            var unnamed = items.Where(item => !named.Contains(item)).ToList();
            return
            [
                .. Ordered(named, item => ((string)item["workflow"]!["name"]!).ToLowerInvariant(), descending),
                .. Ordered(unnamed, _ => string.Empty, descending),
            ];
        }

        Func<JsonNode, string> leading = sort switch
        {
            "time" => _ => string.Empty,
            "level" => item => LevelRank[(string)item["level"]!].ToString("D2", CultureInfo.InvariantCulture),
            "source" => item => (string)item["source"]!,
            "category" => item => (string)item["category"]!,
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "Not a sort the log has."),
        };
        return Ordered(items, leading, descending);
    }

    private static List<string> Ordered(IEnumerable<JsonNode> rows, Func<JsonNode, string> leading, bool descending)
    {
        var ascending = rows
            .OrderBy(leading, StringComparer.Ordinal)
            .ThenBy(item => Place(item).When)
            .ThenBy(item => Place(item).Source)
            .ThenBy(item => Place(item).Number)
            .Select(item => (string)item["id"]!)
            .ToList();
        if (descending)
        {
            ascending.Reverse();
        }

        return ascending;
    }
}
