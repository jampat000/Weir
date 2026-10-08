using Microsoft.Extensions.Logging.Abstractions;
using Weir.Core.Settings;
using Weir.Core.Time;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Auth;
using Weir.Infrastructure.Runtime;
using Weir.Infrastructure.Settings;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.Runtime;

/// <summary>Pause and Resume in the tray reach the server through <c>pause-request.json</c>: they are applied as the tray, recorded in Activity, and the request is taken away.</summary>
public sealed class TrayPauseRequestWatcherTests : IDisposable
{
    private const string Entries =
        "SELECT group_concat(title || ': ' || detail, ' | ') FROM " +
        "(SELECT title, detail FROM activity_events WHERE event_type LIKE 'system.processing_%' ORDER BY id)";

    private readonly StoreFixture _store = new();
    private readonly SuitePauseService _pause;
    private readonly TrayPauseRequestWatcher _watcher;

    public TrayPauseRequestWatcherTests()
    {
        var changes = new DataChangePublisher();
        _pause = new SuitePauseService(new SuiteSettingsStore(new AuthStore(), changes), new ActivityStore(), changes);
        _watcher = new TrayPauseRequestWatcher(_store.Options, _store.Database, _pause, _store.Clock, NullLogger<TrayPauseRequestWatcher>.Instance);
    }

    private string RequestPath => Path.Join(_store.Options.WeirHome, TrayPauseRequestWatcher.FileName);

    public void Dispose()
    {
        _watcher.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        _watcher.Dispose();
        _store.Dispose();
    }

    private Task<PauseState> CurrentAsync() =>
        _store.WithUnitOfWork(uow => _pause.CurrentAsync(uow, Timestamp.UtcNow(_store.Clock)));

    private Task<PauseState> ChangeAsync(bool paused, bool keepLooking = true) =>
        _store.WithUnitOfWork(uow => _pause.ChangeAsync(uow, paused, null, keepEnd: false, keepLooking, Timestamp.UtcNow(_store.Clock), "alice"));

    private async Task<string[]> EntriesAsync()
    {
        using var connection = _store.Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = Entries;
        return (await command.ExecuteScalarAsync() as string)?.Split(" | ") ?? [];
    }

    private static string Request(bool paused) => $"{{\"paused\": {(paused ? "true" : "false")}, \"requested_at\": \"2026-01-15T10:00:00Z\"}}";

    /// <summary>Writes the request and waits for the server to take it away, which it does once it has answered.</summary>
    private async Task RequestAsync(string contents)
    {
        await File.WriteAllTextAsync(RequestPath, contents);
        await Eventually.ThatAsync(() => !File.Exists(RequestPath));
    }

    [Fact]
    public async Task A_pause_asked_for_by_the_tray_lasts_until_resumed_and_is_recorded()
    {
        await _watcher.StartAsync(CancellationToken.None);

        await RequestAsync(Request(paused: true));

        var state = await CurrentAsync();
        Assert.True(state.Paused);
        Assert.Null(state.PausedUntil);
        Assert.Equal(["Processing paused: The tray paused processing until you resume it. Weir keeps looking for new files and starts nothing."], await EntriesAsync());
    }

    [Fact]
    public async Task A_resume_asked_for_by_the_tray_is_recorded()
    {
        await ChangeAsync(paused: true);
        await _watcher.StartAsync(CancellationToken.None);

        await RequestAsync(Request(paused: false));

        Assert.False((await CurrentAsync()).Paused);
        Assert.Equal("Processing resumed: The tray resumed processing.", (await EntriesAsync())[^1]);
    }

    [Fact]
    public async Task A_pause_from_the_tray_leaves_the_choice_to_keep_looking_for_new_files_as_it_stands()
    {
        await ChangeAsync(paused: false, keepLooking: false);
        await _watcher.StartAsync(CancellationToken.None);

        await RequestAsync(Request(paused: true));

        var state = await CurrentAsync();
        Assert.True(state.Paused);
        Assert.False(state.ScanWhilePaused);
    }

    [Fact]
    public async Task A_request_left_while_the_server_was_down_is_answered_when_it_starts()
    {
        await File.WriteAllTextAsync(RequestPath, Request(paused: true));

        await _watcher.StartAsync(CancellationToken.None);

        await Eventually.ThatAsync(() => !File.Exists(RequestPath));
        Assert.True((await CurrentAsync()).Paused);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[true]")]
    [InlineData("{\"requested_at\": \"2026-01-15T10:00:00Z\"}")]
    [InlineData("{\"paused\": \"yes\"}")]
    public async Task A_request_that_cannot_be_read_is_ignored_and_removed(string contents)
    {
        await _watcher.StartAsync(CancellationToken.None);

        await RequestAsync(contents);

        Assert.False((await CurrentAsync()).Paused);
        Assert.Empty(await EntriesAsync());
    }
}
