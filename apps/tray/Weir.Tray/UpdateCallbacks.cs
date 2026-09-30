namespace Weir.Tray;

/// <summary>What <see cref="TrayUpdates"/> tells the rest of the tray, all run on the tray's UI thread.</summary>
/// <param name="OnUi">Runs an action on the UI thread.</param>
/// <param name="Changed">The menu state may have changed.</param>
/// <param name="Announce">An update was found or downloaded under this mode; tell the person.</param>
/// <param name="ApplyNow">Install the downloaded update and restart: a person asked for it, or Weir has been idle long enough.</param>
sealed record UpdateCallbacks(Action<Action> OnUi, Action Changed, Action<UpdateMode> Announce, Action ApplyNow);
