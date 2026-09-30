namespace Weir.Tray;

/// <summary>What the server last said about its file work, as far as the tray can trust it.</summary>
enum ServerWork
{
    /// <summary>No answer the tray can rely on: none yet, an unreadable one, or one too old to describe now.</summary>
    Unknown,

    /// <summary>A file pass is running, or one is queued and able to start.</summary>
    Busy,

    /// <summary>No file pass is running, and none is queued that could start.</summary>
    Idle,
}
