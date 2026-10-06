using System.Text.Json;
using System.Text.Json.Nodes;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>Reading JSON for the assertions: key sets, nulls, truthiness and number kinds.</summary>
internal static class SystemPartBJson
{
    public static JsonNode Parse(string json) => JsonNode.Parse(json)!;

    /// <summary>The keys of an object, in the order the server wrote them.</summary>
    public static IReadOnlyList<string> KeysInOrder(JsonNode? node) => node!.AsObject().Select(pair => pair.Key).ToList();

    public static IReadOnlyList<string> Keys(JsonNode? node) => Sorted(KeysInOrder(node));

    public static IReadOnlyList<string> Sorted(IEnumerable<string> values) => [.. values.Order(StringComparer.Ordinal)];

    public static IReadOnlyList<string> Strings(JsonNode? node) => [.. node!.AsArray().Select(item => (string)item!)];

    public static void AssertKeys(IEnumerable<string> expected, JsonNode? actual) =>
        Assert.Equal(Sorted(expected), Keys(actual));

    public static void AssertHasKeys(IEnumerable<string> expected, JsonNode? actual)
    {
        var present = Keys(actual).ToHashSet(StringComparer.Ordinal);
        Assert.True(
            present.IsSupersetOf(expected),
            $"Expected at least {string.Join(", ", expected)} but the keys were {string.Join(", ", present)}");
    }

    public static void AssertSameJson(JsonNode? expected, JsonNode? actual) =>
        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"Expected {expected?.ToJsonString()} but got {actual?.ToJsonString()}");

    /// <summary>The key is there and its value is JSON null (a missing key is not the same thing).</summary>
    public static bool IsNull(JsonNode? parent, string key) =>
        parent is JsonObject fields && fields.ContainsKey(key) && fields[key] is null;

    public static void AssertNull(JsonNode? parent, string key) =>
        Assert.True(IsNull(parent, key), $"Expected {key} to be null in {parent?.ToJsonString()}");

    public static bool IsNumber(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.Number;

    public static bool IsInteger(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<long>(out _);

    public static bool IsString(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.String;

    public static bool IsBool(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() is JsonValueKind.True or JsonValueKind.False;

    /// <summary>Truthiness: not null, not empty, not zero, not false.</summary>
    public static bool IsTruthy(JsonNode? node) => node switch
    {
        null => false,
        JsonObject fields => fields.Count > 0,
        JsonArray elements => elements.Count > 0,
        JsonValue value => value.GetValueKind() switch
        {
            JsonValueKind.String => ((string)value!).Length > 0,
            JsonValueKind.Number => (double)value != 0,
            JsonValueKind.True => true,
            _ => false,
        },
        _ => false,
    };

    public static void AssertTruthy(JsonNode? node, string what) =>
        Assert.True(IsTruthy(node), $"Expected {what} to be set but it was {node?.ToJsonString() ?? "null"}");

    /// <summary>A copy of <paramref name="source"/> with one field replaced.</summary>
    public static JsonObject With(JsonObject source, string key, JsonNode? value)
    {
        var copy = (JsonObject)source.DeepClone();
        copy[key] = value;
        return copy;
    }

    public static JsonArray ArrayOf(params string[] values) => [.. values.Select(value => (JsonNode?)JsonValue.Create(value))];
}
