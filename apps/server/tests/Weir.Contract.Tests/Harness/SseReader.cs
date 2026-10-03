using System.Net;
using System.Text.Json.Nodes;
using Xunit.Sdk;

namespace Weir.Contract.Tests.Harness;

/// <summary>One server-sent event stream, read block by block (a block ends at a blank line).</summary>
public sealed class SseReader : IDisposable
{
    private const string EventField = "event:";
    private const string DataField = "data:";

    // Short enough that a stream that never sends fails the test quickly instead of hanging it; the Python reader's read timeout.
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(10);

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

    /// <summary>
    /// The lines of the next non-empty block. Fails when the stream sends nothing at all for <paramref name="idleTimeout"/>
    /// (10 seconds by default); a test that waits for something slow passes a longer one.
    /// </summary>
    public async Task<IReadOnlyList<string>> NextBlockAsync(TimeSpan? idleTimeout = null)
    {
        var limit = idleTimeout ?? DefaultIdleTimeout;
        using var timeout = new CancellationTokenSource(limit);
        var block = new List<string>();
        while (await ReadLineAsync(timeout, limit) is { } line)
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

    /// <summary>The next named event, skipping <c>retry:</c> and keep-alive comment blocks. The data is whatever JSON the event holds: an object, or an array for <c>system.tasks</c>.</summary>
    public async Task<(string Name, JsonNode Data)> NextEventAsync(TimeSpan? idleTimeout = null)
    {
        while (true)
        {
            var block = await NextBlockAsync(idleTimeout);
            var name = block.FirstOrDefault(line => line.StartsWith(EventField, StringComparison.Ordinal));
            if (name is null)
            {
                continue;
            }

            var data = string.Join('\n', block
                .Where(line => line.StartsWith(DataField, StringComparison.Ordinal))
                .Select(line => line[DataField.Length..].Trim()));
            return (name[EventField.Length..].Trim(), JsonNode.Parse(data)!);
        }
    }

    /// <summary>The data of the next <paramref name="name"/> event, skipping every other one.</summary>
    public async Task<JsonNode> NextEventNamedAsync(string name, TimeSpan? idleTimeout = null)
    {
        while (true)
        {
            var (eventName, data) = await NextEventAsync(idleTimeout);
            if (eventName == name)
            {
                return data;
            }
        }
    }

    private async Task<string?> ReadLineAsync(CancellationTokenSource timeout, TimeSpan limit)
    {
        try
        {
            return await _lines.ReadLineAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new XunitException($"The stream sent nothing for {limit.TotalSeconds:0}s.");
        }
    }
}
