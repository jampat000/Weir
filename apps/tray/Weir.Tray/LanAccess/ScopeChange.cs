namespace Weir.Tray.LanAccess;

/// <summary>What asking the running server to change who it listens for did.</summary>
enum ScopeChange
{
    /// <summary>The server already listened that way, so it was not restarted.</summary>
    Unchanged,

    /// <summary>The server restarted and is healthy with the new scope.</summary>
    Applied,

    /// <summary>The server did not come up with the new scope, and is back the way it was.</summary>
    Failed,
}
