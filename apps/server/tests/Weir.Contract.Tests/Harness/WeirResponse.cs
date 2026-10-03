using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace Weir.Contract.Tests.Harness;

/// <summary>A response as a browser or media manager sees it: status, headers and the whole body.</summary>
public sealed class WeirResponse
{
    private const string SetCookieHeader = "Set-Cookie";

    private readonly HttpResponseMessage _message;
    private readonly Lazy<JsonNode?> _json;

    private WeirResponse(HttpResponseMessage message, byte[] bytes, string text)
    {
        _message = message;
        Bytes = bytes;
        Text = text;
        _json = new Lazy<JsonNode?>(() => JsonNode.Parse(Text));
    }

    public HttpStatusCode Status => _message.StatusCode;

    /// <summary>The body as the server sent it. The client does not decompress, so a compressed body is still compressed here.</summary>
    public byte[] Bytes { get; }

    public string Text { get; }

    /// <summary>The body parsed as JSON; the body must be JSON.</summary>
    public JsonNode Json => _json.Value ?? throw new InvalidOperationException($"The response body is JSON null: {Text}");

    public JsonObject Fields => Json.AsObject();

    public JsonArray Elements => Json.AsArray();

    public static async Task<WeirResponse> ReadAsync(HttpResponseMessage message, CancellationToken cancellationToken)
    {
        // Content is buffered by the first read, so the text comes from the same bytes.
        var bytes = await message.Content.ReadAsByteArrayAsync(cancellationToken);
        var text = await message.Content.ReadAsStringAsync(cancellationToken);
        return new WeirResponse(message, bytes, text);
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

    /// <summary>A header exactly as the server wrote it (not parsed, so not normalised or reordered), or null when absent.</summary>
    public string? RawHeader(string name)
    {
        foreach (var source in new[] { _message.Headers.NonValidated, _message.Content.Headers.NonValidated })
        {
            if (source.TryGetValues(name, out var values))
            {
                return string.Join(", ", values);
            }
        }

        return null;
    }

    /// <summary>The value of the cookie <paramref name="name"/> in this response's <c>Set-Cookie</c> headers, whether or not a browser would keep it.</summary>
    public string? SetCookieValue(string name)
    {
        if (!_message.Headers.TryGetValues(SetCookieHeader, out var lines))
        {
            return null;
        }

        var prefix = name + "=";
        return lines.Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .Select(line => line[prefix.Length..].Split(';')[0])
            .FirstOrDefault();
    }

    /// <summary>The status and body, for an assertion message that says what the server actually answered.</summary>
    public override string ToString() => $"HTTP {(int)Status} {Text}";
}
