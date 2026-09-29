namespace Weir.Host;

/// <summary>
/// The name of the kernel event the Windows tray sets to ask a running server to stop (#833). The tray builds the
/// same name from the server's process id (apps/tray ServerStopRequest), so the two must not drift apart.
/// </summary>
public static class StopRequest
{
    /// <summary>
    /// <c>Local\</c> keeps the event in the creating user's Windows session, and the name carries the process id so
    /// several Weirs on one machine each have their own.
    /// </summary>
    public static string EventName(int processId) => FormattableString.Invariant($"Local\\Weir-Stop-{processId}");

    /// <summary>Makes the server stop itself when its stop event is set.</summary>
    internal static void AddStopRequestListener(this IServiceCollection services, string eventName) =>
        services.AddHostedService(provider => new StopRequestListener(
            provider.GetRequiredService<IHostApplicationLifetime>(),
            provider.GetRequiredService<ILogger<StopRequestListener>>(),
            eventName));
}
