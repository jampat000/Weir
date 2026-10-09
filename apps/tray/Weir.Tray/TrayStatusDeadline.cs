namespace Weir.Tray;

/// <summary>
/// How long the tray waits for a status from a server that is running. The dot blinks until the status says green, amber or
/// red, so a status that never arrives (the file cannot be written or read) would blink for ever; past <see cref="Wait"/> it
/// is overdue, and the tray says so instead.
/// </summary>
sealed class TrayStatusDeadline(Func<DateTimeOffset> now)
{
    /// <summary>How long a running server may go without a readable status.</summary>
    internal static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private DateTimeOffset? _since;

    /// <summary>Follows the state: the wait begins when a server is running with no status, and ends when it has one or stops running.</summary>
    internal void Follow(bool waiting)
    {
        if (!waiting)
        {
            _since = null;
        }
        else
        {
            _since ??= now();
        }
    }

    /// <summary>Whether the wait has run past <see cref="Wait"/>.</summary>
    internal bool Overdue => _since is { } since && now() - since >= Wait;
}
