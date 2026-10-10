using Weir.Infrastructure.Runtime;

namespace Weir.Infrastructure.Tests.Runtime;

/// <summary>What a starting server tells the tray: that it is busy, and why it could not start (#951).</summary>
public sealed class StartupNotesTests : IDisposable
{
    private readonly TempDirectory _home = new();

    public void Dispose() => _home.Dispose();

    [Fact]
    public void An_error_is_a_headline_then_the_whole_sentence()
    {
        StartupNotes.WriteError(_home.Path, "Couldn't save a copy", "Weir couldn't save a copy of its data: the drive is full.");

        Assert.Equal(
            "Couldn't save a copy\nWeir couldn't save a copy of its data: the drive is full.",
            File.ReadAllText(_home.Join(StartupNotes.ErrorFileName)));
    }

    [Fact]
    public void Progress_exists_while_the_server_is_busy_and_goes_when_it_is_not()
    {
        StartupNotes.WriteProgress(_home.Path, "Saving a copy of Weir's data (480 MB) before updating…");

        Assert.Equal("Saving a copy of Weir's data (480 MB) before updating…", File.ReadAllText(_home.Join(StartupNotes.ProgressFileName)));

        StartupNotes.ClearProgress(_home.Path);

        Assert.False(File.Exists(_home.Join(StartupNotes.ProgressFileName)));
    }

    [Fact]
    public void Progress_is_touched_every_ten_seconds_while_the_work_runs_and_removed_when_it_ends()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
        var path = _home.Join(StartupNotes.ProgressFileName);

        var progress = StartupNotes.BeginProgress(_home.Path, "Saving a copy", time);
        File.SetLastWriteTimeUtc(path, time.GetUtcNow().UtcDateTime - TimeSpan.FromMinutes(5));

        time.Advance(StartupNotes.ProgressInterval);
        Assert.Equal(time.GetUtcNow().UtcDateTime, File.GetLastWriteTimeUtc(path));
        Assert.Equal("Saving a copy", File.ReadAllText(path));

        time.Advance(StartupNotes.ProgressInterval);
        Assert.Equal(time.GetUtcNow().UtcDateTime, File.GetLastWriteTimeUtc(path));

        progress.Dispose();
        Assert.False(File.Exists(path));

        time.Advance(StartupNotes.ProgressInterval);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_start_removes_what_the_last_one_left()
    {
        StartupNotes.WriteProgress(_home.Path, "busy");
        StartupNotes.WriteError(_home.Path, "headline", "sentence");

        StartupNotes.ClearStale(_home.Path);

        Assert.Empty(Directory.EnumerateFiles(_home.Path));
    }

    [Fact]
    public void A_note_that_cannot_be_written_never_stops_the_start()
    {
        var missing = _home.Join("not", "there");

        StartupNotes.WriteError(missing, "headline", "sentence");
        StartupNotes.WriteProgress(missing, "busy");
        StartupNotes.ClearStale(missing);
        StartupNotes.ClearProgress(missing);

        Assert.False(Directory.Exists(missing));
    }
}
