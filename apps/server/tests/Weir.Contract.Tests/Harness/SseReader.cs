using System.Net;
using System.Text.Json.Nodes;
using Xunit.Sdk;

namespace Weir.Contract.Tests.Harness;

/// <summary>One server-sent event stream, read block by block (a block ends at a blank line).</summary>
public sealed class SseReader : IDisposable
{
    private const string EventField = "event:";
    private const string DataField = "data:";

    // Short enough that a stream that never sends fails the test quickly instead of hanging it.
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpResponseMessage _response;
    private readonly StreamReader _lines;

    public SseReader(HttpResponseMessage response, Stream body)
    {
        _response = response;
        _lines = new StreamReader(body);
    }

    public HttpStatusCode Status => _response.StatusCode;

    public string? Header(string name) => WeirResponse.HeaderValue(_response, name);

    public void Dispose()
    {
        _lines.Dispose();
        _response.Dispose();
    }

    /// <summary>The lines of the next non-empty block.</summary>
    public async Task<IReadOnlyList<string>> NextBlockAsync()
    {
        using var timeout = new CancellationTokenSource(ReadTimeout);
        var block = new List<string>();
        while (await ReadLineAsync(timeout) is { } line)
        {
            if (line.Length == 0)
            {
                if (block.Count > 0)
                {
                    return block;
                }

                continue;
            }

            block.Add(line);
        }

        throw new XunitException($"The stream ended; partial block [{string.Join(" | ", block)}]");
    }

    private async Task<string?> ReadLineAsync(CancellationTokenSource timeout)
    {
        try
        {
            return await _lines.ReadLineAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new XunitException($"The stream sent nothing for {ReadTimeout.TotalSeconds:0}s.");
        }
    }

    /// <summary>The next named event, skipping <c>retry:</c> and keep-alive comment blocks.</summary>
    public async Task<(string Name, JsonObject Data)> NextEventAsync()
    {
        while (true)
        {
            var block = await NextBlockAsync();
            var name = block.FirstOrDefault(line => line.StartsWith(EventField, StringComparison.Ordinal));
            if (name is null)
            {
                continue;
            }

            var data = string.Join('\n', block
                .Where(line => line.StartsWith(DataField, StringComparison.Ordinal))
                .Select(line => line[DataField.Length..].Trim()));
            return (name[EventField.Length..].Trim(), JsonNode.Parse(data)!.AsObject());
        }
    }

    /// <summary>The data of the next <paramref name="name"/> event, skipping every other one.</summary>
    public async Task<JsonObject> NextEventNamedAsync(string name)
    {
        while (true)
        {
            var (eventName, data) = await NextEventAsync();
            if (eventName == name)
            {
                return data;
            }
        }
    }
}
