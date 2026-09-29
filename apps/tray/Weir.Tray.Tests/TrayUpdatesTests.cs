using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// Downloading an update installs nothing. Velopack's installer stops a Weir that is still running a minute after it
/// starts, so an install is started only when the tray is about to end (#857).
/// </summary>
public sealed class TrayUpdatesTests : IDisposable
{
    private static readonly TimeSpan FinishCeiling = TimeSpan.FromSeconds(30);

    private readonly TempDirectory _home = TempDirectory.AsWeirHome();

    public void Dispose() => _home.Dispose();

    [Theory]
    [InlineData((int)UpdateMode.Auto)]
    [InlineData((int)UpdateMode.DownloadOnly)]
    public async Task A_downloaded_update_is_not_installed_by_the_download(int mode)
    {
        var service = new FakeUpdateService();

        await CheckUntilAnnouncedAsync(service, (UpdateMode)mode);

        Assert.True(service.IsDownloaded);
        Assert.Empty(service.Applied);
    }

    [Fact]
    public async Task A_found_update_is_not_downloaded_when_the_mode_only_notifies()
    {
        var service = new FakeUpdateService();

        await CheckUntilAnnouncedAsync(service, UpdateMode.NotifyOnly);

        Assert.False(service.IsDownloaded);
        Assert.Empty(service.Applied);
    }

    [Fact]
    public async Task The_server_is_told_an_update_is_downloaded_and_waiting()
    {
        await CheckUntilAnnouncedAsync(new FakeUpdateService(), UpdateMode.Auto);

        var state = File.ReadAllText(Path.Combine(_home.Path, "update-state.json"));
        Assert.Contains("\"downloaded\": true", state, StringComparison.Ordinal);
        Assert.Contains("\"version\": \"9.9.9\"", state, StringComparison.Ordinal);
    }

    // Runs a check on the thread pool, as the tray does, and returns once the person has been told about the outcome.
    private async Task CheckUntilAnnouncedAsync(FakeUpdateService service, UpdateMode mode)
    {
        var announced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var updates = new TrayUpdates(
            service,
            _home.Path,
            new UpdateSettings { Mode = mode },
            onUi: action => action(),
            changed: () => { },
            announce: _ => announced.TrySetResult(),
            applyNow: () => { });

        updates.CheckInBackground();

        await announced.Task.WaitAsync(FinishCeiling);
    }
}
