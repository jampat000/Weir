using System.Text.Json;
using System.Text.Json.Nodes;

namespace Weir.LiveAudit;

/// <summary>The status and JSON payload (if any) of a same-origin API call made from the signed-in page.</summary>
internal sealed record ApiResult(int Status, JsonNode? Payload)
{
    public static ApiResult From(JsonElement element)
    {
        var status = element.GetProperty("status").GetInt32();
        var payload = element.TryGetProperty("payload", out var node) && node.ValueKind != JsonValueKind.Null
            ? JsonNode.Parse(node.GetRawText())
            : null;
        return new ApiResult(status, payload);
    }

    public override string ToString() => $"{Status} {Payload?.ToJsonString()}";
}
