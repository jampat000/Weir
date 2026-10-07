namespace Weir.Contract.Tests.Harness;

/// <summary>
/// Recognises, from what a server wrote before it exited, that it could not listen because another process already
/// held its port. Kestrel reports that as "Failed to bind to address ...: address already in use." with an
/// <c>AddressInUseException</c> inside it, the same on Windows and Linux. A bind that failed for another reason (no
/// permission, an address that does not exist) is not a clash and is not matched.
/// </summary>
internal static class ServerBindFailure
{
    private static readonly string[] PortInUseMarkers = ["address already in use", "AddressInUseException"];

    public static bool IsPortInUse(string serverLog) =>
        PortInUseMarkers.Any(marker => serverLog.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
