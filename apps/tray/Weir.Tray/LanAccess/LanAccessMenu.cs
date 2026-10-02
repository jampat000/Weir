using Weir.Tray.Firewall;

namespace Weir.Tray.LanAccess;

/// <summary>
/// The tray's two LAN access items, "Allow other devices on your network..." and "Only allow this PC", and what
/// they do. Everything here runs on the UI thread: the menu handlers start it there, and the work that leaves it
/// (the Windows admin prompt, the server restart) hands back to it when it finishes.
/// </summary>
sealed class LanAccessMenu : IDisposable
{
    private readonly LanAccessSync _sync;
    private readonly ServerHost _server;
    private readonly Action<LanAccessNotice> _notify;
    private readonly CancellationToken _cancellationToken;
    private readonly ToolStripMenuItem _allowItem = new();
    private readonly ToolStripMenuItem _thisPcOnlyItem = new();

    internal LanAccessMenu(LanAccessSync sync, ServerHost server, Action<LanAccessNotice> notify, CancellationToken cancellationToken)
    {
        _sync = sync;
        _server = server;
        _notify = notify;
        _cancellationToken = cancellationToken;

        _allowItem.Click += (_, _) => BackgroundWork.Observe("Allow other devices on your network", AllowAsync());
        _thisPcOnlyItem.Click += (_, _) => BackgroundWork.Observe("Only allow this PC", LimitToThisPcAsync());
        Show(LanAccessActivity.Idle);
    }

    public void Dispose()
    {
        _allowItem.Dispose();
        _thisPcOnlyItem.Dispose();
    }

    internal void AddTo(ToolStripItemCollection items)
    {
        items.Add(_allowItem);
        items.Add(_thisPcOnlyItem);
    }

    /// <summary>Another process (the web page, <c>--allow-lan</c>) saved a choice that needs the Windows admin prompt.</summary>
    internal void OnWaitingForWindows() => Show(LanAccessActivity.WaitingForWindows);

    /// <summary>Another process changed the choice and the tray has carried it out, or could not.</summary>
    internal void OnChangedElsewhere(SavedChoiceApplied applied)
    {
        Show(LanAccessActivity.Idle);
        _notify(LanAccessNotice.ForSavedChoice(applied));
    }

    // Blocks on the elevated child process (and the UAC prompt the person answers) off the UI thread, so the tray's
    // message loop never freezes on it.
    private async Task AllowAsync()
    {
        TrayLog.Write("Allow other devices on your network: requested from the tray menu.");
        Show(LanAccessActivity.WaitingForWindows);
        try
        {
            var outcome = await Task.Run(() => FirewallElevation.ConfigureElevated(TrayLog.Write), _cancellationToken);
            if (outcome != FirewallElevation.Outcome.Configured)
            {
                _notify(LanAccessNotice.FirewallStepFailed(outcome, _server.Scope));
                return;
            }
            Show(LanAccessActivity.RestartingForOtherDevices);
            var change = await _sync.SetAsync(ListenScope.OtherDevices, _cancellationToken);
            _notify(change == ScopeChange.Unchanged
                ? LanAccessNotice.FirewallAllowsWeir()
                : LanAccessNotice.Restarted(ListenScope.OtherDevices, change));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TrayLog.Write($"LAN access: the choice could not be saved ({ex.Message}).");
            _notify(LanAccessNotice.NotSaved());
        }
        catch (OperationCanceledException)
        {
            TrayLog.Write("Allow other devices on your network: stopped, because Weir is quitting or updating.");
        }
        finally
        {
            Show(LanAccessActivity.Idle);
        }
    }

    private async Task LimitToThisPcAsync()
    {
        TrayLog.Write("Only allow this PC: requested from the tray menu.");
        Show(LanAccessActivity.RestartingForThisPcOnly);
        try
        {
            var change = await _sync.SetAsync(ListenScope.ThisPcOnly, _cancellationToken);
            _notify(LanAccessNotice.Restarted(ListenScope.ThisPcOnly, change));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TrayLog.Write($"LAN access: the choice could not be saved ({ex.Message}).");
            _notify(LanAccessNotice.NotSaved());
        }
        catch (OperationCanceledException)
        {
            TrayLog.Write("Only allow this PC: stopped, because Weir is quitting or updating.");
        }
        finally
        {
            Show(LanAccessActivity.Idle);
        }
    }

    private void Show(LanAccessActivity activity)
    {
        var state = LanAccessMenuState.Describe(_server.Scope, activity);
        _allowItem.Text = state.AllowText;
        _allowItem.Enabled = state.AllowEnabled;
        _thisPcOnlyItem.Text = state.ThisPcOnlyText;
        _thisPcOnlyItem.Enabled = state.ThisPcOnlyEnabled;
    }
}
