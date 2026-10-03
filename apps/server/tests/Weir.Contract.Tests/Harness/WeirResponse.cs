using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace Weir.Contract.Tests.Harness;

/// <summary>A response as a browser or media manager sees it: status, headers and the whole body.</summary>
public sealed class WeirResponse
{
    private readonly HttpResponseMessage _message;
    private readonly Lazy<JsonNode?> _json;

    private WeirResponse(HttpResponseMessage message, string text)
    {
        _message = message;
        Text = text;
        _json = new Lazy<JsonNode?>(() => JsonNode.Parse(Text));
    }

    public HttpStatusCode Status => _message.StatusCode;

    public string Text { get; }

    /// <summary>The body parsed as JSON; the body must be JSON.</summary>
    public JsonNode Json => _json.Value ?? throw new InvalidOperationException($"The response body is JSON null: {Text}");

    public JsonObject Fields => Json.AsObject();

    public JsonArray Elements => Json.AsArray();

    public static async Task<WeirResponse> ReadAsync(HttpResponseMessage message, CancellationToken cancellationToken)
    {
        var text = await message.Content.ReadAsStringAsync(cancellationToken);
        return new WeirResponse(message, text);
    }

    public static string? HeaderValue(HttpResponseMessage message, string name)
    {
        IEnumerable<HttpHeaders> sources = [message.Headers, message.Content.Headers];
        foreach (var source in sources)
        {
            if (source.TryGetValues(name, out var values))
            {
                return string.Join(", ", values);
            }
        }

        return null;
    }

    public string? Header(string name) => HeaderValue(_message, name);

    /// <summary>The status and body, for an assertion message that says what the server actually answered.</summary>
    public override string ToString() => $"HTTP {(int)Status} {Text}";
}
