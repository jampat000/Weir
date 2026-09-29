namespace Weir.Tray.LanAccess;

/// <summary>The part of the running server that <see cref="LanAccessSync"/> steers.</summary>
interface IServerListenScope
{
    ListenScope Scope { get; }

    /// <summary>Restarts the server for <paramref name="to"/>; goes back to the old scope if it does not come up.</summary>
    Task<ScopeChange> MoveToScopeAsync(ListenScope to, CancellationToken cancellationToken);
}

/// <summary>
/// Keeps the running server listening the way the saved LAN access choice says. The tray's own menu goes through
/// <see cref="SetAsync"/>; a change made by another process (<c>--allow-lan</c>) is picked up by
/// <see cref="WatchAsync"/>, which restarts the server to match. The saved choice and the running server never
/// disagree for long: a server that will not start the new way puts the saved choice back.
/// </summary>
sealed class LanAccessSync(string runtimeHome, IServerListenScope server, TimeProvider time) : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    // Held while a choice is saved and applied, or while the saved choice is checked against the server, so the
    // menu and the watcher never both restart the server for the same change.
    private readonly SemaphoreSlim _changing = new(1, 1);

    public void Dispose() => _changing.Dispose();

    /// <summary>Saves <paramref name="scope"/> and restarts the server to match.</summary>
    internal async Task<ScopeChange> SetAsync(ListenScope scope, CancellationToken cancellationToken)
    {
        await _changing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LanAccessSetting.Write(runtimeHome, scope);
            return await ApplyAsync(scope, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _changing.Release();
        }
    }

    /// <summary>
    /// Restarts the server whenever the saved choice is something the server is not doing, and reports each
    /// restart to <paramref name="onRestarted"/>.
    /// </summary>
    internal async Task WatchAsync(Action<ListenScope, ScopeChange> onRestarted, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval, time);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await CheckAsync(onRestarted, cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task CheckAsync(Action<ListenScope, ScopeChange> onRestarted, CancellationToken cancellationToken)
    {
        await _changing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (LanAccessSetting.Read(runtimeHome, log: _ => { }) is not { } scope || scope == server.Scope)
            {
                return;
            }
            var change = await ApplyAsync(scope, cancellationToken).ConfigureAwait(false);
            if (change != ScopeChange.Unchanged)
            {
                onRestarted(scope, change);
            }
        }
        finally
        {
            _changing.Release();
        }
    }

    private async Task<ScopeChange> ApplyAsync(ListenScope scope, CancellationToken cancellationToken)
    {
        var change = await server.MoveToScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        if (change == ScopeChange.Failed)
        {
            PutSavedChoiceBack();
        }
        return change;
    }

    private void PutSavedChoiceBack()
    {
        try
        {
            LanAccessSetting.Write(runtimeHome, server.Scope);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TrayLog.Write($"LAN access: the saved choice could not be put back ({ex.Message}).");
        }
    }
}
