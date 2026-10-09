using System.Globalization;
using Weir.Tray.LanAccess;

namespace Weir.Tray;

/// <summary>Where the bundled server is, as far as the tray knows.</summary>
enum ServerPhase
{
    /// <summary>Being started, or restarted on purpose; not ready yet.</summary>
    Starting,

    /// <summary>Ready and answering.</summary>
    Running,

    /// <summary>Not running and not coming back by itself: it could not start, or the watchdog gave up.</summary>
    Stopped,
}

/// <summary>The one corner mark the tray icon carries, if any. No mark means all is well.</summary>
enum TrayBadge
{
    None,
    Starting,
    Paused,
    NeedsYou,
    UpdateReady,
}

/// <summary>
/// What the tray says about Weir right now: its corner mark, its status line and its hover text. One mark shows at a time,
/// by priority: needs you, then starting, then paused, then update ready. The status line is the same words as the hover
/// text without the product name, so the menu and the hover never disagree.
/// </summary>
/// <param name="Phase">Where the server is.</param>
/// <param name="Server">What tray-status.json says, or null when the server has written none. Used only while the server runs.</param>
/// <param name="UpdateVersion">The version of an update that is downloaded and waiting to install, or null.</param>
/// <param name="Port">The port the server is on.</param>
sealed record TrayState(ServerPhase Phase, TrayStatus? Server, string? UpdateVersion, int Port)
{
    /// <summary>The longest text a notification-area icon can show on Windows.</summary>
    internal const int HoverLimit = 127;

    private const string Product = "Weir";
    private const string Ellipsis = "…";

    private TrayStatus? Reported => Phase == ServerPhase.Running ? Server : null;

    internal bool IsPaused => Reported is { Paused: true };

    internal TrayBadge Badge =>
        Phase == ServerPhase.Stopped || Reported is { NeedsYou: true } ? TrayBadge.NeedsYou
        : Phase == ServerPhase.Starting ? TrayBadge.Starting
        : IsPaused ? TrayBadge.Paused
        : UpdateVersion is not null ? TrayBadge.UpdateReady
        : TrayBadge.None;

    /// <summary>The state in a line: what Weir is doing, then what needs the person.</summary>
    internal string StatusLine => string.Join(" - ", [Headline, .. NeedsYouParts]);

    /// <summary>The icon's hover text: the product name and the status line, never over <see cref="HoverLimit"/> characters.</summary>
    internal string HoverText => Fit($"{Product} - {StatusLine}");

    internal string Address => AddressFor(Port);

    internal static string AddressFor(int port) => string.Create(CultureInfo.InvariantCulture, $"http://localhost:{port}");

    /// <summary>
    /// The address "Copy address" puts on the clipboard: with other devices allowed in, the one they would type, this PC's
    /// name; otherwise this PC's own.
    /// </summary>
    internal static string AddressToCopy(ListenScope scope, int port, string machineName) =>
        scope == ListenScope.OtherDevices
            ? string.Create(CultureInfo.InvariantCulture, $"http://{machineName}:{port}")
            : AddressFor(port);

    private string Headline => this switch
    {
        { Phase: ServerPhase.Stopped } => "Stopped - choose Restart Weir",
        { Phase: ServerPhase.Starting } => "Starting...",
        { Reported.ServerOk: false } => "Needs attention - choose Open logs folder",
        { IsPaused: true } => "Paused",
        { UpdateVersion: { } version } => $"Update ready ({version})",
        _ => $"Running at {Address}",
    };

    private IEnumerable<string> NeedsYouParts
    {
        get
        {
            if (Reported is not { ServerOk: true } status)
            {
                yield break;
            }
            if (status.FilesNeedingYou > 0)
            {
                yield return status.FilesNeedingYou == 1 ? "1 file needs a look" : $"{status.FilesNeedingYou} files need a look";
            }
            if (status.ManagersUnreachable.Count > 0)
            {
                yield return status.ManagersUnreachable.Count == 1
                    ? $"{status.ManagersUnreachable[0]} can't be reached"
                    : $"{status.ManagersUnreachable.Count} media managers can't be reached";
            }
        }
    }

    // Cut at a word, never in the middle of one, so a long connection name costs the end of the line and nothing else.
    private static string Fit(string text)
    {
        if (text.Length <= HoverLimit)
        {
            return text;
        }
        var cut = text.LastIndexOf(' ', HoverLimit - Ellipsis.Length);
        var kept = text.AsSpan(0, cut > 0 ? cut : HoverLimit - Ellipsis.Length).TrimEnd(" -");
        return string.Concat(kept, Ellipsis);
    }
}
