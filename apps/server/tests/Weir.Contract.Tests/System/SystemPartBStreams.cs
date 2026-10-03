using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>The Activity stream, as the System tests read it.</summary>
internal static class SystemPartBStreams
{
    private const string StreamPath = SystemPartBHelpers.Api + "/activity/stream";
    private const string EventField = "event:";
    private const string DataField = "data:";

    public static Task<SseReader> OpenAsync(WeirClient client) => client.OpenStreamAsync(StreamPath);

    /// <summary>Reads the frames every stream opens with, so it is certainly listening when the test acts.</summary>
    public static async Task AssertOpenedAsync(SseReader stream)
    {
        Assert.Equal(["retry: 5000"], await stream.NextBlockAsync());
        await stream.NextEventNamedAsync("activity.latest");
    }

    /// <summary>The data of the next <paramref name="name"/> event, whatever JSON it holds (the reader's own helper expects an object).</summary>
    public static async Task<JsonNode> NextFrameNamedAsync(SseReader stream, string name)
    {
        while (true)
        {
            var block = await stream.NextBlockAsync();
            var eventName = block.FirstOrDefault(line => line.StartsWith(EventField, StringComparison.Ordinal));
            if (eventName is null || eventName[EventField.Length..].Trim() != name)
            {
                continue;
            }

            var data = string.Join('\n', block
                .Where(line => line.StartsWith(DataField, StringComparison.Ordinal))
                .Select(line => line[DataField.Length..].Trim()));
            return JsonNode.Parse(data)!;
        }
    }
}
