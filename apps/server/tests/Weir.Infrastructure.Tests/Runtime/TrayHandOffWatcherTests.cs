using Microsoft.Extensions.Logging.Abstractions;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Runtime;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.Runtime;

/// <summary>The tray's answers reach the open screens when the tray writes them: a downloaded update, and the firewall prompt's outcome.</summary>
public sealed class TrayHandOffWatcherTests : IAsyncLifetime, IDisposable
{
    private static readonly TimeSpan LongEnoughToHearNothing = TimeSpan.FromMilliseconds(800);

    private readonly StoreFixture _store = new();
    private readonly DataChangePublisher _changes = new();
    private TrayHandOffWatcher? _watcher;

    private string Home => _store.Options.WeirHome;

    public async Task InitializeAsync()
    {
        _watcher = new TrayHandOffWatcher(_store.Options, _changes, NullLogger<TrayHandOffWatcher>.Instance);
        await _watcher.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _watcher!.StopAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _store.Dispose();
    }

    private static async Task<string> NextAsync(BroadcastSubscription<string> heard)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await foreach (var topic in heard.ReadAllAsync(timeout.Token))
        {
            return topic;
        }

        throw new InvalidOperationException("The subscription ended.");
    }

    /// <summary>The first <paramref name="count"/> topics, however long the watcher takes to settle, then anything else that follows soon after.</summary>
    private static async Task<string[]> HeardAsync(BroadcastSubscription<string> heard, int count)
    {
        var topics = new List<string>();
        for (var i = 0; i < count; i++)
        {
            topics.Add(await NextAsync(heard));
        }

        topics.AddRange(await HeardAfterAsync(heard, LongEnoughToHearNothing));
        return [.. topics];
    }

    private static async Task<string[]> HeardAfterAsync(BroadcastSubscription<string> heard, TimeSpan wait)
    {
        await Task.Delay(wait);
        heard.Dispose();
        var topics = new List<string>();
        await foreach (var topic in heard.ReadAllAsync(CancellationToken.None))
        {
            topics.Add(topic);
        }

        return [.. topics];
    }

    [Fact]
    public async Task An_update_the_tray_downloaded_is_announced()
    {
        using var heard = _changes.Subscribe();

        await File.WriteAllTextAsync(Path.Join(Home, UpdateFiles.StateFileName), "{\"downloaded\": true, \"version\": \"9.9.9\"}");

        Assert.Equal(DataTopics.Update, await NextAsync(heard));
    }

    [Fact]
    public async Task A_choice_saved_for_the_tray_is_announced_even_when_it_is_the_same_choice()
    {
        var choice = new LanAccessFile(Home);
        choice.Write(NetworkScope.Network);
        using var heard = _changes.Subscribe();

        choice.Write(NetworkScope.Network);

        Assert.Equal(DataTopics.NetworkAccess, await NextAsync(heard));
    }

    [Fact]
    public async Task A_burst_of_writes_is_announced_once()
    {
        using var heard = _changes.Subscribe();

        for (var write = 0; write < 5; write++)
        {
            await File.WriteAllTextAsync(Path.Join(Home, UpdateFiles.StateFileName), $"{{\"downloaded\": true, \"version\": \"9.9.{write}\"}}");
        }

        Assert.Equal([DataTopics.Update], await HeardAsync(heard, 1));
    }

    [Fact]
    public async Task Each_file_announces_its_own_data()
    {
        using var heard = _changes.Subscribe();

        await File.WriteAllTextAsync(Path.Join(Home, UpdateFiles.StateFileName), "{\"downloaded\": false}");
        await File.WriteAllTextAsync(Path.Join(Home, LanAccessFile.FileName), "on");

        Assert.Equal(
            [DataTopics.NetworkAccess, DataTopics.Update],
            [.. (await HeardAsync(heard, 2)).Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public async Task Files_the_tray_does_not_answer_through_are_not_announced()
    {
        using var heard = _changes.Subscribe();

        await File.WriteAllTextAsync(Path.Join(Home, UpdateFiles.WorkStateFileName), "{}");
        await File.WriteAllTextAsync(Path.Join(Home, "notes.txt"), "not a hand-off");

        Assert.Empty(await HeardAfterAsync(heard, LongEnoughToHearNothing));
    }

    [Fact]
    public async Task A_file_written_whole_and_renamed_into_place_is_announced_by_its_own_name()
    {
        using var heard = _changes.Subscribe();

        File.WriteAllText(Path.Join(Home, ".update-state.json.1a2b3c4d.tmp"), "{\"downloaded\": true, \"version\": \"9.9.9\"}");
        File.Move(Path.Join(Home, ".update-state.json.1a2b3c4d.tmp"), Path.Join(Home, UpdateFiles.StateFileName), overwrite: true);

        Assert.Equal([DataTopics.Update], await HeardAsync(heard, 1));
    }
}
