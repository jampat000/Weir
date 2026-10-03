using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>The Activity stream, as the System tests read it.</summary>
internal static class SystemPartBStreams
{
    private const string StreamPath = SystemPartBHelpers.Api + "/activity/stream";

    public static Task<SseReader> OpenAsync(WeirClient client) => client.OpenStreamAsync(StreamPath);

    /// <summary>Reads the frames every stream opens with, so it is certainly listening when the test acts.</summary>
    public static async Task AssertOpenedAsync(SseReader stream)
    {
        Assert.Equal(["retry: 5000"], await stream.NextBlockAsync());
        await stream.NextEventNamedAsync("activity.latest");
    }
}
