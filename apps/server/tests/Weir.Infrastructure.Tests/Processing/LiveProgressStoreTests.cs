using Microsoft.Extensions.Logging.Abstractions;
using Weir.Core.Json;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Processing.RemuxPass;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.Processing;

/// <summary>
/// What Live reads about a file being worked on (docs/archive/live-and-library.md), written by
/// <see cref="ActivityProgressReporter"/> and read back by <see cref="LiveProgressStore"/>.
/// </summary>
public sealed class LiveProgressStoreTests
{
    private static PyDict Writing(string path, double percent) => new PyDict()
        .Set("status", "processing")
        .Set("relative_media_path", path)
        .Set("percent", percent)
        .Set("eta_seconds", 31.0)
        .Set("elapsed_seconds", 12L)
        .Set("speed", "148x")
        .Set("removed_audio", new PyList([PyJson.Of("spa aac 2ch: removed"), PyJson.Of("fre aac 2ch: removed")]))
        .Set("removed_subtitles", new PyList([PyJson.Of("ger subrip: removed")]))
        .Set("message", "Weir is writing the cleaned-up file.");

    private static async Task<Dictionary<string, LiveProgress>> ReadAsync(StoreFixture store)
    {
        await using var uow = await UnitOfWork.OpenAsync(store.Database);
        return await LiveProgressStore.ByPathAsync(uow, store.Clock);
    }

    [Fact]
    public async Task A_working_file_carries_its_step_speed_and_what_is_coming_out()
    {
        using var store = new StoreFixture();
        var reporter = new ActivityProgressReporter(store.Database, 7, new PyDict(), NullLogger.Instance, store.Clock);

        reporter.Report(Writing("Show/S01E01.mkv", 42.5));

        var progress = (await ReadAsync(store))["Show/S01E01.mkv"];
        Assert.Equal("processing", progress.Status);
        Assert.Equal(42.5, progress.Percent);
        Assert.Equal(31.0, progress.EtaSeconds);
        Assert.Equal(12.0, progress.ElapsedSeconds);
        Assert.Equal("148x", progress.Speed);
        Assert.Equal(["spa aac 2ch: removed", "fre aac 2ch: removed"], progress.RemovedAudio);
        Assert.Equal(["ger subrip: removed"], progress.RemovedSubtitles);
    }

    [Fact]
    public async Task The_final_checks_read_as_finishing()
    {
        using var store = new StoreFixture();
        var reporter = new ActivityProgressReporter(store.Database, 8, new PyDict(), NullLogger.Instance, store.Clock);

        reporter.Report(Writing("Film (2024)/Film.mkv", 99));
        reporter.Report(new PyDict()
            .Set("status", "finishing")
            .Set("relative_media_path", "Film (2024)/Film.mkv")
            .Set("percent", 100.0)
            .Set("message", "The cleaned-up file was written. Weir is doing final safety checks."));

        var progress = (await ReadAsync(store))["Film (2024)/Film.mkv"];
        Assert.Equal("finishing", progress.Status);
        Assert.Equal(100.0, progress.Percent);
    }

    [Fact]
    public async Task A_pass_longer_than_two_minutes_keeps_its_progress_while_it_is_still_reporting()
    {
        // The row is inserted once and rewritten, so created_at is when the pass started. Judging staleness on
        // created_at dropped every pass longer than two minutes: a big file's progress vanished part-way.
        // created_at comes from SQLite's own clock, so the test clock has to agree with it for the old rule to bite.
        using var store = new StoreFixture();
        store.Clock.Set(DateTimeOffset.UtcNow);
        var reporter = new ActivityProgressReporter(store.Database, 9, new PyDict(), NullLogger.Instance, store.Clock);
        reporter.Report(Writing("Big/Big.mkv", 5));
        await store.WithUnitOfWork(async uow =>
        {
            // The pass started half an hour ago.
            await uow.ExecuteAsync("UPDATE activity_events SET created_at = datetime(created_at, '-30 minutes')");
            return 0;
        });

        reporter.Report(Writing("Big/Big.mkv", 64));

        var progress = (await ReadAsync(store))["Big/Big.mkv"];
        Assert.Equal(64, progress.Percent);
    }

    [Fact]
    public async Task A_pass_that_stopped_reporting_drops_out()
    {
        using var store = new StoreFixture();
        var reporter = new ActivityProgressReporter(store.Database, 10, new PyDict(), NullLogger.Instance, store.Clock);
        reporter.Report(Writing("Gone/Gone.mkv", 30));

        store.Clock.Set(store.Clock.GetUtcNow().AddMinutes(3));

        Assert.False((await ReadAsync(store)).ContainsKey("Gone/Gone.mkv"));
    }
}
