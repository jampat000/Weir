using System.Text.Json.Nodes;

namespace Weir.Contract.Tests.Harness.Fakes;

/// <summary>One request a fake server received.</summary>
/// <param name="Method">Upper-case.</param>
/// <param name="Path">The path as sent, without the query.</param>
/// <param name="Query">Values by name; a parameter with an empty value is left out.</param>
/// <param name="Headers">By name, compared without regard to case.</param>
/// <param name="Body">The raw body; empty when there was none.</param>
/// <param name="At">When it arrived.</param>
public sealed record RecordedRequest(
    string Method,
    string Path,
    Dictionary<string, string[]> Query,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body,
    DateTimeOffset At)
{
    /// <summary>The body as JSON, or null when there was none.</summary>
    public JsonNode? Json => Body.Length == 0 ? null : JsonNode.Parse(Body);

    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}
