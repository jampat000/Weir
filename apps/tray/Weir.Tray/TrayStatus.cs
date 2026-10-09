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

/// <summary>One look at tray-status.json: what it held and when the server wrote it.</summary>
/// <param name="Status">What the file says, or null when there is no file or it holds nothing usable.</param>
/// <param name="WrittenAtUtc">When the file was last written.</param>
/// <param name="Failed">Whether the file could not be read this time.</param>
readonly record struct TrayStatusReading(TrayStatus? Status, DateTime? WrittenAtUtc, bool Failed)
{
    internal static TrayStatusReading Missing { get; } = new(null, null, false);

    internal static TrayStatusReading Unreadable { get; } = new(null, null, true);
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

    /// <summary>
    /// What the file holds now: <see cref="TrayStatusReading.Missing"/> when there is none, or
    /// <see cref="TrayStatusReading.Unreadable"/> when it cannot be read this time (the server is replacing it, or it is
    /// damaged), which says nothing about the status and is never a reason to change what is shown. The file is opened so
    /// that the server can still rename a new one over it while it is read.
    /// </summary>
    internal static TrayStatusReading Read(string runtimeHome, Action<string> log)
    {
        var path = Path.Combine(runtimeHome, FileName);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var wrote = File.GetLastWriteTimeUtc(path);
            return new TrayStatusReading(Parse(JsonSerializer.Deserialize<Wire>(stream, Json)), wrote, Failed: false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return TrayStatusReading.Missing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            log($"{FileName} could not be read ({ex.Message}).");
            return TrayStatusReading.Unreadable;
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
