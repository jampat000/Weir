using System.Text.Json.Nodes;

namespace Weir.Contract.Tests.MediaManagers;

/// <summary>Small stateless helpers for reading and building the JSON these tests exchange.</summary>
internal static class JsonFields
{
    public static int Id(JsonNode row) => (int)row["id"]!;

    public static bool IsWholeNumber(JsonNode? value) => value is JsonValue number && number.TryGetValue<long>(out _);

    /// <summary>The field is present and null: the server says "none" rather than leaving the field out.</summary>
    public static void AssertNull(JsonObject row, string field)
    {
        Assert.True(row.ContainsKey(field), $"the answer has no {field} field: {row.ToJsonString()}");
        Assert.Null(row[field]);
    }

    public static HashSet<string> Names(JsonObject row) => [.. row.Select(field => field.Key)];

    /// <summary>Copies every field of <paramref name="source"/> onto <paramref name="target"/>, replacing fields of the same name.</summary>
    public static JsonObject Merge(JsonObject target, JsonObject? source)
    {
        foreach (var (name, value) in source ?? [])
        {
            target[name] = value?.DeepClone();
        }

        return target;
    }
}
