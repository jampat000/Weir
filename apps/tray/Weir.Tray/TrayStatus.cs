using System.Text.Json;

namespace Weir.Tray;

/// <summary>What the server says about itself for the tray: whether processing is paused, what waits on the person.</summary>
sealed record TrayStatus(
    bool Paused,
    DateTimeOffset? PausedUntil,
    int FilesNeedingYou,
    IReadOnlyList<string> ManagersUnreachable,
    bool ServerOk)
{
    /// <summary>Whether anything in this status needs the person: files waiting on them, a manager out of reach, a sick server.</summary>
    internal bool NeedsYou => !ServerOk || FilesNeedingYou > 0 || ManagersUnreachable.Count > 0;
}

/// <summary>
/// tray-status.json in the runtime home, written by the server (atomically, whenever its content changes) for the tray
/// to show: the paused state and what needs the person. A file, like work-state.json, because the tray has no signed-in
/// session and the server should have no route that answers without one.
/// </summary>
static class TrayStatusFile
{
    internal const string FileName = "tray-status.json";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>The status the server wrote, or null when there is none to show: no file yet, or one that cannot be read.</summary>
    internal static TrayStatus? Read(string runtimeHome, Action<string> log)
    {
        var path = Path.Combine(runtimeHome, FileName);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            return Parse(JsonSerializer.Deserialize<Wire>(File.ReadAllText(path), Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            log($"{FileName} could not be read ({ex.Message}); showing no status from the server.");
            return null;
        }
    }

    private static TrayStatus? Parse(Wire? wire)
    {
        if (wire is null)
        {
            return null;
        }
        var managers = (wire.NeedsYou?.ManagersUnreachable ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToList();
        return new TrayStatus(
            wire.Paused ?? false,
            wire.PausedUntil,
            Math.Max(0, wire.NeedsYou?.Files ?? 0),
            managers,
            wire.ServerOk ?? true);
    }

    private sealed record Wire(bool? Paused, DateTimeOffset? PausedUntil, WireNeedsYou? NeedsYou, bool? ServerOk);

    private sealed record WireNeedsYou(int? Files, List<string>? ManagersUnreachable);
}
