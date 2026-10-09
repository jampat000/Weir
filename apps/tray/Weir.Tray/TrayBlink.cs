namespace Weir.Tray;

/// <summary>
/// The blink of the starting dot: lit and unlit by turns for as long as the dot is <see cref="TrayDot.Starting"/>, and still,
/// lit, from the first dot that says green, amber or red. It only decides; the tray's timer on the UI thread calls
/// <see cref="Tick"/> and swaps between icons it already made, so a blink draws nothing and holds no new handle.
/// </summary>
sealed class TrayBlink
{
    /// <summary>How long the dot stays lit, and how long unlit.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    /// <summary>Whether the dot is blinking.</summary>
    internal bool Active { get; private set; }

    /// <summary>Whether the dot is drawn right now.</summary>
    internal bool Lit { get; private set; } = true;

    /// <summary>Follows the dot: blinking begins lit when the dot is starting, and ends, lit, with the first dot that is known.</summary>
    internal void Follow(TrayDot dot)
    {
        var starting = dot == TrayDot.Starting;
        if (starting == Active)
        {
            return;
        }
        Active = starting;
        Lit = true;
    }

    /// <summary>One interval has passed. Returns whether the icon changes with it.</summary>
    internal bool Tick()
    {
        if (!Active)
        {
            return false;
        }
        Lit = !Lit;
        return true;
    }
}
