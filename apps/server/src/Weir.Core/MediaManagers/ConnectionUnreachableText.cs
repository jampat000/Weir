namespace Weir.Core.MediaManagers;

/// <summary>
/// The one sentence every screen uses when Weir cannot reach a media manager or download client at all. It names the
/// address so the operator can compare it with where the app really is; why the connection failed is a technical
/// detail that belongs in the logs, never here.
/// </summary>
public static class ConnectionUnreachableText
{
    public static string For(string label, string baseUrl) =>
        $"Weir could not reach {label} at {baseUrl}. " +
        "Check the address is right, and that the app is running and reachable from this machine.";
}
