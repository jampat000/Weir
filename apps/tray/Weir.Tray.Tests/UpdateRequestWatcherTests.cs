using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Weir.Tray.Tests;

/// <summary>The tray hears of a request from System › About when the server leaves its flag file, with no timer to wait for.</summary>
public sealed class UpdateRequestWatcherTests : IDisposable
{
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(20);

    private readonly TempDirectory _home = TempDirectory.Create();
    private readonly FakeTimeProvider _clock = new();
    private readonly BlockingCollection<int> _looks = [];
    private readonly BlockingCollection<string> _logged = [];
    private int _looked;

    public void Dispose()
    {
        _looks.Dispose();
        _logged.Dispose();
        _home.Dispose();
    }

    private UpdateRequestWatcher StartWatching()
    {
        var watcher = new UpdateRequestWatcher(_home.Path, () => _looks.Add(Interlocked.Increment(ref _looked)), _logged.Add, _clock);
        watcher.Start();
        return watcher;
    }

    private void NextLook() =>
        Assert.True(_looks.TryTake(out _, Ceiling), "The watcher never looked.");

    private string NextLogged() =>
        _logged.TryTake(out var line, Ceiling) ? line : throw new TimeoutException("The watcher never logged.");

    [Theory]
    [InlineData("update-check-now")]
    [InlineData("update-download-now")]
    [InlineData("update-apply-now")]
    public void A_flag_the_server_leaves_is_looked_for_once_the_burst_has_settled(string flag)
    {
        using var watcher = StartWatching();
        NextLook();

        File.WriteAllText(Path.Combine(_home.Path, flag), string.Empty);

        NextLook();
    }

    [Fact]
    public void Starting_looks_once_at_once_for_a_flag_left_before()
    {
        File.WriteAllText(Path.Combine(_home.Path, "update-check-now"), string.Empty);

        using var watcher = StartWatching();

        NextLook();
    }

    [Fact]
    public void A_watcher_that_lost_events_looks_again_and_says_so()
    {
        using var watcher = StartWatching();
        NextLook();

        watcher.OnWatcherError(new InternalBufferOverflowException("Too many changes at once."));

        NextLook();
        Assert.Contains("Too many changes at once.", NextLogged(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_watcher_that_keeps_failing_is_logged_once_a_minute_but_looks_each_time()
    {
        using var watcher = StartWatching();
        NextLook();

        watcher.OnWatcherError(new IOException("First."));
        watcher.OnWatcherError(new IOException("Second."));
        NextLook();
        Assert.Contains("First.", NextLogged(), StringComparison.Ordinal);
        _clock.Advance(UpdateRequestWatcher.ErrorLogInterval);
        watcher.OnWatcherError(new IOException("Third."));

        Assert.Contains("Third.", NextLogged(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_watcher_has_room_for_a_burst_of_writes_and_can_be_disposed_twice()
    {
        var watcher = StartWatching();

        Assert.True(watcher.BufferBytes >= 64 * 1024);
        watcher.Dispose();
        watcher.Dispose();
    }
}
