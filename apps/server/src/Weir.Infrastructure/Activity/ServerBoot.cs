namespace Weir.Infrastructure.Activity;

/// <summary>
/// Which run of the server this is. Every start makes a new id and the stream announces it when it opens, so a page that
/// reconnects and hears a different id knows the server restarted.
/// </summary>
public sealed class ServerBoot
{
    /// <summary>The id of this run: a GUID made when the server started.</summary>
    public string Id { get; } = Guid.NewGuid().ToString("D");
}
