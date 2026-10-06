using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Libraries;

/// <summary>Assertions the part B libraries tests share; each failure says what the server actually answered.</summary>
internal static class LibrariesPartBChecks
{
    public static void Status(WeirResponse response, HttpStatusCode expected) =>
        Assert.True(response.Status == expected, response.ToString());

    /// <summary>The object carries every one of <paramref name="names"/> (it may carry more).</summary>
    public static void HasKeys(JsonObject body, params string[] names)
    {
        var missing = names.Where(name => !body.ContainsKey(name)).ToArray();
        Assert.True(missing.Length == 0, $"Missing {string.Join(", ", missing)} in {body.ToJsonString()}");
    }

    /// <summary>The key is present and its value is JSON null.</summary>
    public static void IsNull(JsonObject body, string name)
    {
        Assert.True(body.ContainsKey(name), $"{name} is missing from {body.ToJsonString()}");
        Assert.Null(body[name]);
    }

    public static string[] Strings(JsonNode? array) => [.. array!.AsArray().Select(item => (string)item!)];
}
