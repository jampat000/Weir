using Weir.Core.Json;
using Weir.Core.Time;

namespace Weir.Infrastructure.Runtime;

/// <summary>
/// What the Windows tray shows about Weir on its icon, in its hover text and on its Pause item, kept in
/// <see cref="FileName"/> in Weir's data folder. The server rewrites the file whenever one of these changes, and the tray
/// watches it; no route carries it, so only a process that can open the data folder can read it. The file is one JSON object:
/// <code>
/// { "paused": false, "paused_until": null,
///   "unreachable": { "managers": ["Deluno on RIG"], "folders": ["The watched folder for Movies"] },
///   "server_ok": true }
/// </code>
/// <c>paused_until</c> is an ISO 8601 UTC time while a timed pause is running and null otherwise. <c>unreachable</c> is what the
/// tray's dot is amber for, each in the words the tray shows: <c>managers</c> names the enabled media managers whose last
/// connection test got no answer, as the Connections screen names them, and <c>folders</c> names the watched, work and output
/// folders of the switched-on workflows that Weir cannot reach. Nothing about files or downloads is in the file. <c>server_ok</c>
/// is true from the moment the server starts until it stops cleanly; a server that was killed leaves it true.
/// </summary>
/// <param name="Paused">Processing is paused right now.</param>
/// <param name="PausedUntil">When a timed pause ends, in UTC; null for a pause that lasts until it is resumed, and when not paused.</param>
/// <param name="ManagersUnreachable">The names of the enabled media managers that do not answer.</param>
/// <param name="FoldersUnreachable">The workflow folders Weir cannot reach, each as "The watched folder for Movies".</param>
/// <param name="ServerOk">The server is running.</param>
public sealed record TrayStatus(
    bool Paused, DateTime? PausedUntil, IReadOnlyList<string> ManagersUnreachable, IReadOnlyList<string> FoldersUnreachable, bool ServerOk)
{
    public const string FileName = "tray-status.json";

    /// <summary>The file's contents.</summary>
    public string ToJson() => WireJsonWriter.Dumps(
        new WireObject()
            .Set("paused", Paused)
            .Set("paused_until", PausedUntil is { } until ? Timestamp.FromUtc(until).ToWireText() : null)
            .Set(
                "unreachable",
                new WireObject()
                    .Set("managers", Names(ManagersUnreachable))
                    .Set("folders", Names(FoldersUnreachable)))
            .Set("server_ok", ServerOk),
        WireJsonFormat.Compact);

    private static WireArray Names(IReadOnlyList<string> names) => new(names.Select(name => WireValue.Of(name)));
}
