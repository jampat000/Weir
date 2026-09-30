namespace Weir.Tray;

/// <summary>
/// How a downloaded update is handed to Velopack's updater: whether it shows its own progress window, whether Weir is
/// started again afterwards, and with which arguments. Every hand-over is silent, because the person is at the
/// tray menu or nobody is there at all, and a second install window helps neither (#869). A restart is never a person
/// opening Weir (#638): in Automatic mode nobody may be at the computer, and "Restart to update" is pressed in a
/// browser already showing Weir, so neither opens a window.
/// </summary>
readonly record struct UpdateHandOver(bool Silent, bool Restart, string[] RestartArguments)
{
    /// <summary>Install the update and leave Weir stopped.</summary>
    internal static UpdateHandOver ThenStayStopped { get; } = new(Silent: true, Restart: false, RestartArguments: []);

    /// <summary>Install the update and start Weir again without opening the browser.</summary>
    internal static UpdateHandOver ThenRestart { get; } = new(Silent: true, Restart: true, RestartArguments: [Program.NoBrowserArgument]);
}
