using Microsoft.Extensions.Time.Testing;
using Weir.Tray.LanAccess;
using Xunit;

namespace Weir.Tray.Tests.LanAccess;

/// <summary>
/// The running server follows the saved LAN access choice: a change from the menu or from <c>--allow-lan</c>
/// restarts it, no change leaves it alone, and a server that will not start the new way does not leave the saved
/// choice saying something that is not true.
/// </summary>
public sealed class LanAccessSyncTests : IDisposable
{
    private readonly TempDirectory _temp = TempDirectory.AsWeirHome();
    private readonly FakeTimeProvider _time = new();

    public void Dispose() => _temp.Dispose();

    private string Home => _temp.Path;

    private LanAccessSync SyncFor(FakeServer server) => new(Home, server, _time);

    private ListenScope? Saved() => LanAccessSetting.Read(Home, _ => { });

    private sealed class FakeServer(ListenScope scope) : IServerListenScope
    {
        public ListenScope Scope { get; private set; } = scope;

        public ScopeChange ResultOfARealMove { get; set; } = ScopeChange.Applied;

        public List<ListenScope> Moves { get; } = [];

        public Task<ScopeChange> MoveToScopeAsync(ListenScope to, CancellationToken cancellationToken)
        {
            Moves.Add(to);
            if (to == Scope)
            {
                return Task.FromResult(ScopeChange.Unchanged);
            }
            if (ResultOfARealMove == ScopeChange.Applied)
            {
                Scope = to;
            }
            return Task.FromResult(ResultOfARealMove);
        }
    }

    // -- Saving from the tray's own menu ---------------------------------------

    [Fact]
    public async Task Setting_a_choice_saves_it_and_restarts_the_server_to_match()
    {
        var server = new FakeServer(ListenScope.ThisPcOnly);
        using var sync = SyncFor(server);

        var change = await sync.SetAsync(ListenScope.OtherDevices, CancellationToken.None);

        Assert.Equal(ScopeChange.Applied, change);
        Assert.Equal(ListenScope.OtherDevices, server.Scope);
        Assert.Equal(ListenScope.OtherDevices, Saved());
    }

    [Fact]
    public async Task Setting_the_choice_the_server_already_has_does_not_restart_it()
    {
        var server = new FakeServer(ListenScope.OtherDevices);
        using var sync = SyncFor(server);

        var change = await sync.SetAsync(ListenScope.OtherDevices, CancellationToken.None);

        Assert.Equal(ScopeChange.Unchanged, change);
        Assert.Equal(ListenScope.OtherDevices, Saved());
    }

    [Fact]
    public async Task A_server_that_will_not_start_the_new_way_leaves_the_old_choice_saved()
    {
        var server = new FakeServer(ListenScope.ThisPcOnly) { ResultOfARealMove = ScopeChange.Failed };
        using var sync = SyncFor(server);

        var change = await sync.SetAsync(ListenScope.OtherDevices, CancellationToken.None);

        Assert.Equal(ScopeChange.Failed, change);
        Assert.Equal(ListenScope.ThisPcOnly, Saved());
    }

    // -- Noticing a change made by another process (--allow-lan) -------------------

    [Fact]
    public async Task A_choice_another_process_saved_restarts_the_server_and_is_reported()
    {
        var server = new FakeServer(ListenScope.ThisPcOnly);
        using var sync = SyncFor(server);
        LanAccessSetting.Write(Home, ListenScope.OtherDevices);
        var reported = new List<(ListenScope Scope, ScopeChange Change)>();

        await sync.CheckAsync((scope, change) => reported.Add((scope, change)), CancellationToken.None);

        Assert.Equal(ListenScope.OtherDevices, server.Scope);
        Assert.Equal([(ListenScope.OtherDevices, ScopeChange.Applied)], reported);
    }

    [Fact]
    public async Task A_saved_choice_the_server_already_follows_leaves_it_alone()
    {
        var server = new FakeServer(ListenScope.OtherDevices);
        using var sync = SyncFor(server);
        LanAccessSetting.Write(Home, ListenScope.OtherDevices);

        await sync.CheckAsync((_, _) => throw new Xunit.Sdk.XunitException("Nothing changed, so nothing is reported."), CancellationToken.None);

        Assert.Empty(server.Moves);
    }

    [Fact]
    public async Task With_no_choice_saved_the_server_is_left_alone()
    {
        var server = new FakeServer(ListenScope.OtherDevices);
        using var sync = SyncFor(server);

        await sync.CheckAsync((_, _) => throw new Xunit.Sdk.XunitException("Nothing changed, so nothing is reported."), CancellationToken.None);

        Assert.Empty(server.Moves);
    }

    [Fact]
    public async Task A_change_the_server_cannot_follow_is_reported_once_and_the_old_choice_is_put_back()
    {
        var server = new FakeServer(ListenScope.ThisPcOnly) { ResultOfARealMove = ScopeChange.Failed };
        using var sync = SyncFor(server);
        LanAccessSetting.Write(Home, ListenScope.OtherDevices);
        var reported = new List<ScopeChange>();

        await sync.CheckAsync((_, change) => reported.Add(change), CancellationToken.None);
        await sync.CheckAsync((_, change) => reported.Add(change), CancellationToken.None);

        Assert.Equal([ScopeChange.Failed], reported);
        Assert.Equal(ListenScope.ThisPcOnly, Saved());
        Assert.Single(server.Moves);
    }

    [Fact]
    public async Task Watching_notices_a_change_when_the_poll_interval_passes()
    {
        var server = new FakeServer(ListenScope.ThisPcOnly);
        using var sync = SyncFor(server);
        using var stop = new CancellationTokenSource();
        var restarted = new TaskCompletionSource<ListenScope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watching = sync.WatchAsync((scope, _) => restarted.TrySetResult(scope), stop.Token);

        LanAccessSetting.Write(Home, ListenScope.OtherDevices);
        _time.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(ListenScope.OtherDevices, await restarted.Task);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watching);
    }
}
