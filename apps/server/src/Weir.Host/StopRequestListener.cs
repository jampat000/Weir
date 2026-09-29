namespace Weir.Host;

/// <summary>
/// Stops the server through the normal host shutdown when the tray sets the process's stop event (#833), so hosted
/// services, running jobs and the database close in order rather than the process being killed.
/// </summary>
/// <remarks>
/// The server has no window and no console, so a close message or Ctrl+C cannot reach it, and an HTTP shutdown route
/// would be a new way in from the network. A named event is reachable only by processes of the same Windows user in
/// the same session: it is created with that user's default access control and lives in the <c>Local\</c> namespace.
/// </remarks>
internal sealed class StopRequestListener : BackgroundService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<StopRequestListener> _logger;
    private readonly string _eventName;
    private EventWaitHandle? _stopEvent;

    public StopRequestListener(IHostApplicationLifetime lifetime, ILogger<StopRequestListener> logger, string eventName)
    {
        _lifetime = lifetime;
        _logger = logger;
        _eventName = eventName;
    }

    /// <summary>Creates the event before the server reports ready, so a stop request does not find it missing.</summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var stopEvent = new EventWaitHandle(initialState: false, EventResetMode.ManualReset, _eventName, out var created);
        if (!created)
        {
            // Whoever made the event first decides when it is set, so this server must not act on it.
            stopEvent.Dispose();
            _logger.LogWarning("The stop event {EventName} already exists, so Weir cannot be asked to stop through it.", _eventName);
            return Task.CompletedTask;
        }

        _stopEvent = stopEvent;
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await WaitUntilSetAsync(_stopEvent!, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        _logger.LogInformation("Weir was asked to stop by its tray.");
        _lifetime.StopApplication();
    }

    public override void Dispose()
    {
        _stopEvent?.Dispose();
        base.Dispose();
    }

    private static async Task WaitUntilSetAsync(WaitHandle handle, CancellationToken cancellationToken)
    {
        var set = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = ThreadPool.RegisterWaitForSingleObject(
            handle,
            static (state, _) => ((TaskCompletionSource)state!).TrySetResult(),
            set,
            Timeout.Infinite,
            executeOnlyOnce: true);
        try
        {
            await set.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            wait.Unregister(null);
        }
    }
}
