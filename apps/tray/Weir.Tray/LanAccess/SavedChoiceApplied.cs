using Weir.Tray.Firewall;

namespace Weir.Tray.LanAccess;

/// <summary>
/// What the tray did about a LAN access choice another process saved (the web page, <c>--allow-lan</c>): the
/// server restart it led to and, when Weir's firewall rule was missing, how the person answered the Windows admin prompt.
/// </summary>
/// <param name="Scope">The saved choice.</param>
/// <param name="Change">What happened to the running server.</param>
/// <param name="Firewall">The outcome of the admin prompt; null when it was not raised.</param>
sealed record SavedChoiceApplied(ListenScope Scope, ScopeChange Change, FirewallElevation.Outcome? Firewall);

/// <summary>What <see cref="LanAccessSync.WatchAsync"/> reports while it carries out a saved choice.</summary>
/// <param name="AskingWindows">The Windows admin prompt is about to show; the person must answer it on this PC.</param>
/// <param name="Applied">The choice has been carried out, or could not be.</param>
sealed record LanAccessWatch(Action AskingWindows, Action<SavedChoiceApplied> Applied);
