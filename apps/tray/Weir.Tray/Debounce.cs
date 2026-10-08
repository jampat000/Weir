namespace Weir.Tray;

/// <summary>
/// Lets one request through per window. A double-click on the icon raises a click and a double-click, and a person who
/// clicks again before the browser appears asks twice; each is one request to open Weir, answered once.
/// </summary>
sealed class Debounce(TimeSpan window, TimeProvider clock)
{
    private readonly Lock _gate = new();
    private long? _lastAllowed;

    /// <summary>Whether this request is the first in its window; the window then restarts from now.</summary>
    internal bool Allow()
    {
        var now = clock.GetTimestamp();
        lock (_gate)
        {
            if (_lastAllowed is { } last && clock.GetElapsedTime(last, now) < window)
            {
                return false;
            }
            _lastAllowed = now;
            return true;
        }
    }
}
