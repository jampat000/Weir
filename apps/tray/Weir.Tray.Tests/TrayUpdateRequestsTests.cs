using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// System › About asks the tray for a step by creating a flag file in the runtime home: check now, download now, or restart
/// and apply. The tray acts on each once, starts nothing twice, and writes update-state.json as it goes, which is how the
/// page shows "Checking", "Downloading update" and what came of them.
/// </summary>
public sealed class TrayUpdateRequestsTests : IDisposable
{
    private static readonly TimeSpan FinishCeiling = TimeSpan.FromSeconds(30);

    private readonly TempDirectory _home = TempDirectory.AsWeirHome();
    private readonly DelayWatchingTimeProvider _clock = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Channel<UpdateMenuAction> _changes = Channel.CreateUnbounded<UpdateMenuAction>();
    private readonly ConcurrentQueue<UpdateMode> _announced = new();
    private readonly TaskCompletionSource _appliedNow = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TrayUpdates? _updates;

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
        _home.Dispose();
    }

    [Fact(Timeout = 30_000)]
    public async Task A_check_request_checks_at_once_and_writes_what_it_found()
    {
        var service = new FakeUpdateService();
        var updates = Build(service, UpdateMode.NotifyOnly);

        RequestFlag("update-check-now");
        updates.ActOnRequests();
        await UntilSettledAsync();

        Assert.False(File.Exists(FlagPath("update-check-now")));
        Assert.Equal(1, service.Checks);
        Assert.Equal(("idle", false, "9.9.9", null), ReadState());
        Assert.Equal([UpdateMode.NotifyOnly], _announced);
    }

    [Fact(Timeout = 30_000)]
    public async Task The_state_says_checking_while_the_check_runs()
    {
        var release = new TaskCompletionSource();
        var service = new FakeUpdateService { HoldCheck = release.Task };
        var updates = Build(service, UpdateMode.NotifyOnly);

        RequestFlag("update-check-now");
        updates.ActOnRequests();
        Assert.Equal(UpdateMenuAction.Wait, await NextChangeAsync());

        Assert.Equal(("checking", false, null, null), ReadState());

        release.SetResult();
        await UntilSettledAsync();
        Assert.Equal(("idle", false, "9.9.9", null), ReadState());
    }

    [Fact(Timeout = 30_000)]
    public async Task A_check_that_finds_nothing_leaves_the_state_idle_with_no_version()
    {
        var updates = Build(new FakeUpdateService { FindsUpdate = false }, UpdateMode.NotifyOnly);

        RequestFlag("update-check-now");
        updates.ActOnRequests();
        await UntilSettledAsync();

        Assert.Equal(("idle", false, null, null), ReadState());
        Assert.Empty(_announced);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_check_that_fails_writes_the_reason()
    {
        var updates = Build(new FakeUpdateService { CheckFailure = "Weir could not reach GitHub." }, UpdateMode.NotifyOnly);

        RequestFlag("update-check-now");
        updates.ActOnRequests();
        await UntilSettledAsync();

        Assert.Equal(("failed", false, null, "Weir could not reach GitHub."), ReadState());
    }

    [Fact(Timeout = 30_000)]
    public async Task A_check_in_a_mode_that_downloads_goes_on_to_download_and_says_so()
    {
        var release = new TaskCompletionSource();
        var service = new FakeUpdateService { HoldDownload = release.Task };
        var updates = Build(service, UpdateMode.DownloadOnly);

        RequestFlag("update-check-now");
        updates.ActOnRequests();
        Assert.Equal(UpdateMenuAction.Wait, await NextChangeAsync());
        Assert.Equal(UpdateMenuAction.Wait, await NextChangeAsync());

        Assert.Equal(("downloading", false, "9.9.9", null), ReadState());

        release.SetResult();
        await UntilSettledAsync();
        Assert.Equal(("downloaded", true, "9.9.9", null), ReadState());
    }

    [Fact(Timeout = 30_000)]
    public async Task A_download_request_downloads_a_found_update_even_when_the_mode_only_notifies()
    {
        var release = new TaskCompletionSource();
        var service = new FakeUpdateService { HoldDownload = release.Task };
        var updates = Build(service, UpdateMode.NotifyOnly);
        RequestFlag("update-check-now");
        updates.ActOnRequests();
        await UntilSettledAsync();

        RequestFlag("update-download-now");
        updates.ActOnRequests();
        Assert.Equal(UpdateMenuAction.Wait, await NextChangeAsync());
        Assert.Equal(("downloading", false, "9.9.9", null), ReadState());

        release.SetResult();
        await UntilSettledAsync();

        Assert.False(File.Exists(FlagPath("update-download-now")));
        Assert.True(service.IsDownloaded);
        Assert.Equal(("downloaded", true, "9.9.9", null), ReadState());
        Assert.Equal([UpdateMode.NotifyOnly, UpdateMode.DownloadOnly], _announced);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_download_request_finds_the_update_first_when_the_tray_has_not_looked_yet()
    {
        var service = new FakeUpdateService();
        var updates = Build(service, UpdateMode.NotifyOnly);

        RequestFlag("update-download-now");
        updates.ActOnRequests();
        await UntilSettledAsync();

        Assert.Equal((1, 1), (service.Checks, service.Downloads));
        Assert.Equal(("downloaded", true, "9.9.9", null), ReadState());
    }

    [Fact(Timeout = 30_000)]
    public async Task A_download_request_with_nothing_newer_says_so()
    {
        var service = new FakeUpdateService { FindsUpdate = false };
        var updates = Build(service, UpdateMode.NotifyOnly);

        RequestFlag("update-download-now");
        updates.ActOnRequests();
        await UntilSettledAsync();

        Assert.Equal(0, service.Downloads);
        Assert.Equal(("failed", false, null, "There is no newer version of Weir to download."), ReadState());
    }

    [Fact(Timeout = 30_000)]
    public async Task A_download_that_fails_writes_the_reason_and_keeps_the_version()
    {
        var service = new FakeUpdateService { DownloadFailure = "Weir could not download the update." };
        var updates = Build(service, UpdateMode.NotifyOnly);

        RequestFlag("update-download-now");
        updates.ActOnRequests();
        await UntilSettledAsync();

        Assert.Equal(("failed", false, "9.9.9", "Weir could not download the update."), ReadState());
        Assert.False(service.IsDownloaded);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_check_or_download_asked_for_while_one_is_under_way_is_not_started()
    {
        var release = new TaskCompletionSource();
        var service = new FakeUpdateService { HoldCheck = release.Task };
        var updates = Build(service, UpdateMode.NotifyOnly);
        RequestFlag("update-check-now");
        updates.ActOnRequests();
        Assert.Equal(UpdateMenuAction.Wait, await NextChangeAsync());

        await updates.CheckAsync();
        await updates.DownloadAsync();
        release.SetResult();
        await UntilSettledAsync();

        Assert.Equal((1, 0), (service.Checks, service.Downloads));
    }

    [Fact]
    public async Task A_check_does_not_start_while_an_update_is_downloaded_and_waiting()
    {
        var service = new FakeUpdateService().AlreadyDownloaded();
        var updates = Build(service, UpdateMode.NotifyOnly);

        await updates.CheckAsync();

        Assert.Equal(0, service.Checks);
        Assert.True(service.IsDownloaded);
    }

    [Fact]
    public void An_apply_request_hands_the_downloaded_update_over_to_install()
    {
        var updates = Build(new FakeUpdateService().AlreadyDownloaded(), UpdateMode.DownloadOnly);
        RequestFlag("update-apply-now");

        var handedOver = updates.ActOnRequests();

        Assert.True(handedOver);
        Assert.True(_appliedNow.Task.IsCompletedSuccessfully);
        Assert.False(File.Exists(FlagPath("update-apply-now")));
    }

    [Fact]
    public void An_apply_request_waits_until_an_update_has_been_downloaded()
    {
        var updates = Build(new FakeUpdateService(), UpdateMode.DownloadOnly);
        RequestFlag("update-apply-now");

        var handedOver = updates.ActOnRequests();

        Assert.False(handedOver);
        Assert.False(_appliedNow.Task.IsCompleted);
        Assert.True(File.Exists(FlagPath("update-apply-now")));
    }

    [Fact]
    public void Requests_made_while_the_tray_was_not_running_are_dropped_at_start()
    {
        var service = new FakeUpdateService();
        var updates = Build(service, UpdateMode.NotifyOnly);
        RequestFlag("update-check-now");
        RequestFlag("update-download-now");

        updates.Start();

        Assert.False(File.Exists(FlagPath("update-check-now")));
        Assert.False(File.Exists(FlagPath("update-download-now")));
        Assert.Equal((0, 0), (service.Checks, service.Downloads));
    }

    [Fact(Timeout = 30_000)]
    public async Task The_watcher_acts_on_a_request_a_second_after_it_is_made()
    {
        var service = new FakeUpdateService();
        var updates = Build(service, UpdateMode.NotifyOnly);
        updates.Start();
        await _clock.NextDelay();

        RequestFlag("update-check-now");
        _clock.Advance(TimeSpan.FromSeconds(1));
        await UntilSettledAsync();

        Assert.Equal(1, service.Checks);
        Assert.Equal(("idle", false, "9.9.9", null), ReadState());
    }

    private TrayUpdates Build(FakeUpdateService service, UpdateMode mode)
    {
        _updates = new TrayUpdates(
            service,
            _home.Path,
            new UpdateSettings { Mode = mode, CheckOnStartup = false, CheckIntervalMinutes = 0 },
            new UpdateCallbacks(
                OnUi: action => action(),
                Changed: () => _changes.Writer.TryWrite(_updates!.MenuState().Action),
                Announce: _announced.Enqueue,
                ApplyNow: () => _appliedNow.TrySetResult()),
            _clock,
            _shutdown.Token);
        return _updates;
    }

    // The tray tells the menu of every change; the one that finds the tray idle again ends a check or download.
    private async Task<UpdateMenuAction> NextChangeAsync() =>
        await _changes.Reader.ReadAsync().AsTask().WaitAsync(FinishCeiling);

    private async Task UntilSettledAsync()
    {
        while (await NextChangeAsync() == UpdateMenuAction.Wait)
        {
        }
    }

    private string FlagPath(string name) => Path.Combine(_home.Path, name);

    private void RequestFlag(string name) => File.WriteAllText(FlagPath(name), string.Empty);

    private (string? State, bool? Downloaded, string? Version, string? Failure) ReadState()
    {
        var state = JsonNode.Parse(File.ReadAllText(FlagPath("update-state.json")))!;
        return (state["state"]?.GetValue<string>(), state["downloaded"]?.GetValue<bool>(), state["version"]?.GetValue<string>(), state["failure"]?.GetValue<string>());
    }
}
