using System.Collections.Concurrent;
using Xunit;

namespace Weir.Tray.Tests;

/// <summary>The tray hears of a new tray-status.json when the server writes it, with no timer to wait for.</summary>
public sealed class TrayStatusWatcherTests : IDisposable
{
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(20);

    private readonly TempDirectory _home = TempDirectory.AsWeirHome();
    private readonly BlockingCollection<TrayStatus?> _heard = [];

    public void Dispose()
    {
        _heard.Dispose();
        _home.Dispose();
    }

    private TrayStatus? NextHeard() =>
        _heard.TryTake(out var status, Ceiling) ? status : throw new TimeoutException("The watcher never reported.");

    private TrayStatusWatcher StartWatching()
    {
        var watcher = new TrayStatusWatcher(_home.Path, _heard.Add);
        watcher.Start();
        return watcher;
    }

    [Fact]
    public void Starting_reports_a_file_that_is_already_there()
    {
        File.WriteAllText(Path.Combine(_home.Path, TrayStatusFile.FileName), """{ "paused": true }""");

        using var watcher = StartWatching();

        Assert.True(NextHeard()!.Paused);
    }

    [Fact]
    public void Starting_with_no_file_reports_no_status()
    {
        using var watcher = StartWatching();

        Assert.Null(NextHeard());
    }

    [Fact]
    public void A_status_the_server_writes_whole_and_renames_into_place_is_reported_when_it_lands()
    {
        using var watcher = StartWatching();
        Assert.Null(NextHeard());

        AtomicFile.WriteAllText(_home.Path, TrayStatusFile.FileName, """{ "paused": true, "needs_you": { "files": 3 } }""");

        TrayStatus? latest;
        do
        {
            latest = NextHeard();
        }
        while (latest is null);
        Assert.True(latest.Paused);
        Assert.Equal(3, latest.FilesNeedingYou);
    }

    [Fact]
    public void A_burst_of_writes_ends_in_the_last_status()
    {
        using var watcher = StartWatching();
        Assert.Null(NextHeard());

        for (var files = 1; files <= 5; files++)
        {
            AtomicFile.WriteAllText(_home.Path, TrayStatusFile.FileName, $$"""{ "needs_you": { "files": {{files}} } }""");
        }

        var last = NextHeard();
        while (last is not { FilesNeedingYou: 5 })
        {
            last = NextHeard();
        }
        Assert.Equal(5, last.FilesNeedingYou);
    }

    [Fact]
    public void A_file_the_server_removes_is_reported_as_no_status()
    {
        File.WriteAllText(Path.Combine(_home.Path, TrayStatusFile.FileName), "{}");
        using var watcher = StartWatching();
        Assert.NotNull(NextHeard());

        File.Delete(Path.Combine(_home.Path, TrayStatusFile.FileName));

        Assert.Null(NextHeard());
    }
}
