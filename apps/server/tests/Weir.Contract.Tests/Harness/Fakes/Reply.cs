using System.Text;
using System.Text.Json.Nodes;

namespace Weir.Contract.Tests.Harness.Fakes;

/// <summary>A scripted answer: a status, an optional JSON body, optional extra headers. No body means no payload at all.</summary>
public sealed record Reply(int Status = 200, JsonNode? Body = null, IReadOnlyDictionary<string, string>? Headers = null)
{
    /// <summary>200 with this JSON body.</summary>
    public static Reply Ok(JsonNode? body) => new(200, body);

    internal HttpAnswer ToAnswer() => Body is null
        ? new HttpAnswer(Status, [], Headers: Headers)
        : new HttpAnswer(Status, Encoding.UTF8.GetBytes(Body.ToJsonString()), "application/json", Headers);
}
