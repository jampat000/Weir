using System.Text.Json;

namespace Weir.Tray;

/// <summary>
/// tray-heartbeat.json in the runtime home: when the tray last said it is alive. The server offers System › About's update
/// buttons only while it is fresh, because a flag left for a tray that is not there is never answered. The server does not
/// publish a change when it is written, so it can be written as often as it needs to be.
/// </summary>
static class TrayHeartbeatFile
{
    internal const string FileName = "tray-heartbeat.json";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal static void Write(string runtimeHome, DateTimeOffset at) =>
        AtomicFile.WriteAllText(runtimeHome, FileName, JsonSerializer.Serialize(new { At = at }, Json));
}
