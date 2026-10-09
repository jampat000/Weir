using System.Text.Json;

namespace Weir.Tray;

/// <summary>
/// The tray's side of Pause and Resume: pause-request.json in the runtime home, which the server watches, applies and
/// deletes. A file, like the other hand-offs, so it needs no session, and only a process that can write the runtime home
/// (readable by the account running Weir alone) can make the request.
/// </summary>
static class PauseRequestFile
{
    internal const string FileName = "pause-request.json";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>Asks the server to pause processing until it is resumed, or to resume it.</summary>
    internal static void Write(string runtimeHome, bool paused, DateTimeOffset requestedAt) =>
        AtomicFile.WriteAllText(runtimeHome, FileName, JsonSerializer.Serialize(new Request(paused, requestedAt), Json));

    private sealed record Request(bool Paused, DateTimeOffset RequestedAt);
}
