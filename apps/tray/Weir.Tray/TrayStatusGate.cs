namespace Weir.Tray;

/// <summary>
/// The status the tray believes: what tray-status.json says, but only what the server it launched wrote. The server leaves
/// the file behind when it stops (saying it is not well), and the tray reads it at launch, so a file is accepted only if it
/// was written no earlier than the start of the server process the tray started. And it is forgotten whenever the server
/// starts again, so nothing from the run before can show.
/// </summary>
sealed class TrayStatusGate(Func<DateTime?> serverStartedUtc)
{
    private readonly Lock _gate = new();
    private TrayStatus? _current;

    /// <summary>What the server's own file says now, or null when it has said nothing yet.</summary>
    internal TrayStatus? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>The server is being started: whatever it said before no longer holds.</summary>
    internal void ServerStarting()
    {
        lock (_gate)
        {
            _current = null;
        }
    }

    /// <summary>A look at the file. Anything the running server did not write is not taken.</summary>
    internal void Offer(TrayStatusReading reading)
    {
        var accepted = reading.Status is not null
            && reading.WrittenAtUtc is { } wrote
            && serverStartedUtc() is { } started
            && wrote >= started;
        lock (_gate)
        {
            _current = accepted ? reading.Status : null;
        }
    }
}
