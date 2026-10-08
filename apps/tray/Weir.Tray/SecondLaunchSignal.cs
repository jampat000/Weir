namespace Weir.Tray;

/// <summary>
/// How a second launch reaches the Weir that is already running. The second process has no icon of its own to speak
/// from, so it sets a named event and exits, and the running tray, which waits on the event, tells the person from its
/// own icon. The name is per Windows session, like the tray's single-instance mutex.
/// </summary>
sealed class SecondLaunchSignal : IDisposable
{
    internal const string DefaultName = @"Local\WeirTraySecondLaunch";

    private readonly EventWaitHandle _event;
    private readonly RegisteredWaitHandle _registration;

    /// <summary>Calls <paramref name="launched"/>, on a thread-pool thread, each time a second launch signals.</summary>
    internal SecondLaunchSignal(Action launched, string name = DefaultName)
    {
        _event = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        _registration = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) => launched(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Tells the running tray that Weir was started again. Does nothing when no tray is listening.</summary>
    internal static void Raise(string name = DefaultName)
    {
        if (EventWaitHandle.TryOpenExisting(name, out var existing))
        {
            using (existing)
            {
                existing.Set();
            }
        }
    }

    public void Dispose()
    {
        _registration.Unregister(null);
        _event.Dispose();
    }
}
