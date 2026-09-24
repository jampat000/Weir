using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.Processing;

/// <summary>
/// The watched-folder watcher, including the two behaviours issue #552 asks for: an overflow/error restarts
/// the watcher and forces an immediate scan, and readiness reports the watched libraries. One test per
/// admission path drives a real temp folder and a real <see cref="FileSystemWatcher"/>, tagged
/// <c>Category=Integration</c>, since that is the only way to prove the OS actually raises the events Weir
/// subscribes to; every other test drives the debounce and reconcile-tick logic through
/// <see cref="FakeWatcherService"/> and a shared <see cref="FakeTimeProvider"/>, so nothing here depends on
/// real chunk-write timing landing on the right side of a debounce window.
/// </summary>
public sealed class ProcessingWatchedFolderWatcherServiceTests
{
    private const string ScanJobKind = ProcessingWatchedFolderScanDispatchJobKinds.ScanDispatch;

    private static async Task<long> CreateLibraryAsync(
        StoreFixture store, string watched, string output, bool fileSystemEventsEnabled = true)
    {
        await using var uow = await UnitOfWork.OpenAsync(store.Database);
        var seeded = await LibraryStore.SeededForScopeAsync(uow, ProcessingMediaScopes.Movie) ?? throw new InvalidOperationException("No seeded Movies library.");
        var updated = await LibraryStore.UpdateAsync(uow, seeded, new ProcessingLibraryInput
        {
            Name = seeded.Name,
            MediaType = ProcessingMediaScopes.Movie,
            WatchedFolder = watched,
            OutputFolder = output,
            FileSystemEventsEnabled = fileSystemEventsEnabled,
        });
        await uow.CommitAsync();
        return updated.Id;
    }

    private static List<string> ScanJobPayloads(StoreFixture store)
    {
        using var connection = store.Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM jobs WHERE job_kind = @kind";
        command.Parameters.AddWithValue("@kind", ScanJobKind);
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
        }

        return rows;
    }

    private static ProcessingWatchedFolderWatcherService Service(StoreFixture store, WatcherStateStore state, TimeProvider? time = null) =>
        new(store.Database, store.Options, new ProcessingJobStore(store.Database, time ?? TimeProvider.System), state,
            time ?? TimeProvider.System, NullLogger<ProcessingWatchedFolderWatcherService>.Instance);

    // --- one admission implementation: a settled burst becomes one scan --------------------------------

    [Trait("Category", "Integration")]
    [Fact]
    public async Task A_file_appearing_becomes_a_candidate_without_waiting_for_the_scan_interval()
    {
        using var store = new StoreFixture(
            ("WEIR_CREDENTIALS_SECRET", "watcher-tests-secret-1"),
            ("WEIR_PROCESSING_WATCHER_DEBOUNCE_SECONDS", "1"));
        var watched = store.Home.Join("watch");
        var output = store.Home.Join("out");
        Directory.CreateDirectory(watched);
        Directory.CreateDirectory(output);
        var libraryId = await CreateLibraryAsync(store, watched, output);

        var state = new WatcherStateStore();
        var service = Service(store, state);
        await service.StartAsync(CancellationToken.None);
        try
        {
            // Wait for the watcher itself to report Watching rather than guessing how long the first
            // reconcile tick takes: on a loaded CI box that tick can take longer than any fixed sleep, and
            // writing the file before FileSystemWatcher.EnableRaisingEvents is actually true means Windows
            // never raises the event at all, so the test would wait out its full timeout for nothing.
            await Eventually.ThatAsync(() => state.Reports().Any(r => r.LibraryId == libraryId && r.Status == WatcherStatus.Watching));
            await File.WriteAllBytesAsync(Path.Combine(watched, "Gate Test 2001.mkv"), new byte[2048]);

            await Eventually.ThatAsync(() => ScanJobPayloads(store).Count > 0);
            var payloads = ScanJobPayloads(store);
            Assert.Single(payloads);
            var body = (PyDict)PyJsonParser.Parse(payloads[0]);
            Assert.Equal("filesystem_event", ((PyStr)body.Get("scan_trigger")!).Value);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task A_file_moved_into_a_watched_folder_becomes_a_candidate()
    {
        // The common shape: a download client writes elsewhere and moves the finished file in.
        using var store = new StoreFixture(
            ("WEIR_CREDENTIALS_SECRET", "watcher-tests-secret-2"),
            ("WEIR_PROCESSING_WATCHER_DEBOUNCE_SECONDS", "1"));
        var watched = store.Home.Join("watch");
        var output = store.Home.Join("out");
        var staging = store.Home.Join("staging");
        Directory.CreateDirectory(watched);
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(staging);
        var staged = Path.Combine(staging, "Gate Test 2001.mkv");
        await File.WriteAllBytesAsync(staged, new byte[2048]);
        var libraryId = await CreateLibraryAsync(store, watched, output);

        var state = new WatcherStateStore();
        var service = Service(store, state);
        await service.StartAsync(CancellationToken.None);
        try
        {
            // See the comment in A_file_appearing_becomes_a_candidate_without_waiting_for_the_scan_interval:
            // wait for the watcher to actually be attached instead of guessing with a fixed sleep. The move
            // is within store.Home, so it raises Renamed (not Created); the service subscribes to both.
            await Eventually.ThatAsync(() => state.Reports().Any(r => r.LibraryId == libraryId && r.Status == WatcherStatus.Watching));
            File.Move(staged, Path.Combine(watched, "Gate Test 2001.mkv"));

            await Eventually.ThatAsync(() => ScanJobPayloads(store).Count > 0);
            Assert.Single(ScanJobPayloads(store));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_growing_file_written_in_chunks_produces_one_scan_not_one_per_chunk()
    {
        // A PVR (or a downloader) writing parts of a file every so often is one arrival. The chunk writes
        // are real (proving the OS coalesces them into the debounce the way production expects), but the
        // debounce clock is fake and frozen while they happen, so every chunk lands at the same "now" -
        // there is no 1 s real-time boundary a slow CI box could straddle.
        using var store = new StoreFixture(
            ("WEIR_CREDENTIALS_SECRET", "watcher-tests-secret-3"),
            ("WEIR_PROCESSING_WATCHER_DEBOUNCE_SECONDS", "1"));
        var watched = store.Home.Join("watch");
        var output = store.Home.Join("out");
        Directory.CreateDirectory(watched);
        Directory.CreateDirectory(output);
        var libraryId = await CreateLibraryAsync(store, watched, output);

        var time = new FakeTimeProvider();
        var state = new WatcherStateStore();
        var service = Service(store, state, time);
        await service.StartAsync(CancellationToken.None);
        try
        {
            // See the comment in A_file_appearing_becomes_a_candidate_without_waiting_for_the_scan_interval.
            await Eventually.ThatAsync(() => state.Reports().Any(r => r.LibraryId == libraryId && r.Status == WatcherStatus.Watching));
            var target = Path.Combine(watched, "Recording.mkv");
            await using (var handle = File.Open(target, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                for (var i = 0; i < 10; i++)
                {
                    var chunk = new byte[4096];
                    await handle.WriteAsync(chunk);
                    await handle.FlushAsync();
                }
            }

            // Every chunk shares the same recorded "changed" instant, so exactly one tick a full debounce
            // later can possibly drain it. Advance a tick at a time while polling (rather than one big jump)
            // so this does not race the real FileSystemWatcher event, which can still take a moment to reach
            // pending.Note after the writes above return.
            await Eventually.ThatAsync(
                () =>
                {
                    time.Advance(ProcessingWatchedFolderWatcherService.TickInterval);
                    return ScanJobPayloads(store).Count > 0;
                },
                TimeSpan.FromSeconds(5));
            Assert.Single(ScanJobPayloads(store));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    // --- disabled settings: no watcher --------------------------------------------------------------

    [Fact]
    public async Task A_library_with_events_switched_off_is_not_watched_but_is_reported()
    {
        using var store = new StoreFixture(("WEIR_CREDENTIALS_SECRET", "watcher-tests-secret-4"));
        var watched = store.Home.Join("watch");
        var output = store.Home.Join("out");
        Directory.CreateDirectory(watched);
        Directory.CreateDirectory(output);
        var libraryId = await CreateLibraryAsync(store, watched, output, fileSystemEventsEnabled: false);

        var time = new FakeTimeProvider();
        var state = new WatcherStateStore();
        var service = Service(store, state, time);
        await service.StartAsync(CancellationToken.None);
        try
        {
            await Eventually.ThatAsync(() => state.Reports().Any(r => r.LibraryId == libraryId));
            var report = state.Reports().Single(r => r.LibraryId == libraryId);
            Assert.Equal(WatcherStatus.Disabled, report.Status);
            // Switched off deliberately is not degraded; treating it as such trains people to ignore it.
            Assert.False(report.Degraded);
            Assert.Contains("scan interval", report.Detail, StringComparison.Ordinal);

            await File.WriteAllBytesAsync(Path.Combine(watched, "no-watcher.mkv"), new byte[16]);
            // No watcher was ever created for this library, so forcing several reconcile ticks (rather than
            // hoping a fixed real-time window covered enough of them) still finds nothing to admit.
            await AdvancePastSeveralTicksAsync(time);
            Assert.Empty(ScanJobPayloads(store));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task The_global_kill_switch_starts_no_watcher_at_all()
    {
        using var store = new StoreFixture(
            ("WEIR_CREDENTIALS_SECRET", "watcher-tests-secret-5"),
            ("WEIR_PROCESSING_WATCHER_ENABLED", "0"));
        var watched = store.Home.Join("watch");
        var output = store.Home.Join("out");
        Directory.CreateDirectory(watched);
        Directory.CreateDirectory(output);
        await CreateLibraryAsync(store, watched, output);

        var time = new FakeTimeProvider();
        var state = new WatcherStateStore();
        var service = Service(store, state, time);
        await service.StartAsync(CancellationToken.None);
        try
        {
            await AdvancePastSeveralTicksAsync(time);
            Assert.Empty(state.Reports());

            await File.WriteAllBytesAsync(Path.Combine(watched, "no-watcher.mkv"), new byte[16]);
            await AdvancePastSeveralTicksAsync(time);
            Assert.Empty(ScanJobPayloads(store));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        var (ok, detail) = state.Summary();
        Assert.True(ok);
        Assert.Contains("No libraries", detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Forces several reconcile ticks on a fake clock, one at a time (a single large advance only wakes the
    /// one timer already due, not the ones a tick's own continuation goes on to arm), so a "nothing happened"
    /// assertion reflects several ticks that genuinely ran rather than a hopeful real-time window.
    /// </summary>
    private static async Task AdvancePastSeveralTicksAsync(FakeTimeProvider time)
    {
        for (var i = 0; i < 5; i++)
        {
            time.Advance(ProcessingWatchedFolderWatcherService.TickInterval);
            await Task.Delay(20);
        }
    }

    // --- readiness fields ----------------------------------------------------------------------------

    [Fact]
    public async Task Readiness_lists_the_watched_library_with_the_documented_fields()
    {
        using var store = new StoreFixture(("WEIR_CREDENTIALS_SECRET", "watcher-tests-secret-6"));
        var watched = store.Home.Join("watch");
        var output = store.Home.Join("out");
        Directory.CreateDirectory(watched);
        Directory.CreateDirectory(output);
        var libraryId = await CreateLibraryAsync(store, watched, output);

        var state = new WatcherStateStore();
        var service = Service(store, state);
        await service.StartAsync(CancellationToken.None);
        try
        {
            await Eventually.ThatAsync(() => state.Reports().Any(r => r.LibraryId == libraryId && r.Status == WatcherStatus.Watching));
            var report = state.Reports().Single(r => r.LibraryId == libraryId);
            Assert.Equal("Movies", report.LibraryName);
            Assert.Equal(Path.GetFullPath(watched), Path.GetFullPath(report.WatchedFolder));
            Assert.False(report.Degraded);

            var (ok, detail) = state.Summary();
            Assert.True(ok);
            Assert.Contains("Watching 1 folder for changes", detail, StringComparison.Ordinal);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_folder_that_cannot_be_watched_falls_back_to_polling_and_does_not_throw()
    {
        using var store = new StoreFixture(("WEIR_CREDENTIALS_SECRET", "watcher-tests-secret-7"));
        var missing = store.Home.Join("gone");
        var output = store.Home.Join("out");
        Directory.CreateDirectory(output);
        var libraryId = await CreateLibraryAsync(store, missing, output);

        var state = new WatcherStateStore();
        var service = Service(store, state);
        await service.StartAsync(CancellationToken.None);
        try
        {
            await Eventually.ThatAsync(() => state.Reports().Any(r => r.LibraryId == libraryId));
            var report = state.Reports().Single(r => r.LibraryId == libraryId);
            Assert.Equal(WatcherStatus.PollingFallback, report.Status);
            Assert.True(report.Degraded);
            Assert.Contains("network shares", report.Detail, StringComparison.Ordinal);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    // --- overflow/error: full scan and restart (issue #552's own C#-side addition) ----------------------

    [Fact]
    public async Task An_overflow_or_error_immediately_queues_a_full_scan_and_restarts_the_watcher()
    {
        using var store = new StoreFixture(
            ("WEIR_CREDENTIALS_SECRET", "watcher-tests-secret-8"),
            // A long debounce: if the overflow path used the ordinary debounce instead of bypassing it,
            // this test's short wait below would time out before a scan ever appeared.
            ("WEIR_PROCESSING_WATCHER_DEBOUNCE_SECONDS", "60"));
        var watched = store.Home.Join("watch");
        var output = store.Home.Join("out");
        Directory.CreateDirectory(watched);
        Directory.CreateDirectory(output);
        await CreateLibraryAsync(store, watched, output);

        var service = new FakeWatcherService(store.Database, store.Options, new ProcessingJobStore(store.Database, TimeProvider.System),
            new WatcherStateStore(), TimeProvider.System, NullLogger<ProcessingWatchedFolderWatcherService>.Instance);
        await service.StartAsync(CancellationToken.None);
        try
        {
            await Eventually.ThatAsync(() => service.CreatedFor(watched).Count == 1);
            var first = service.CreatedFor(watched)[0];

            first.RaiseError(new IOException("simulated overflow"));

            await Eventually.ThatAsync(() => ScanJobPayloads(store).Count > 0);
            var body = (PyDict)PyJsonParser.Parse(ScanJobPayloads(store)[0]);
            Assert.Equal("filesystem_event", ((PyStr)body.Get("scan_trigger")!).Value);

            // The watcher for this folder was replaced, not merely left running after the error.
            await Eventually.ThatAsync(() => service.CreatedFor(watched).Count == 2);
            Assert.NotSame(first, service.CreatedFor(watched)[1]);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Raises a synthetic <c>Error</c> event — a real buffer-overflow exception cannot be
    /// triggered deterministically from a test.</summary>
    private sealed class FakeFileSystemWatcher(string path) : FileSystemWatcher(path)
    {
        public void RaiseError(Exception exception) => OnError(new ErrorEventArgs(exception));
    }

    private sealed class FakeWatcherService(
        SqliteDatabase database, Weir.Core.Configuration.WeirOptions options, ProcessingJobStore jobStore, WatcherStateStore state, TimeProvider time,
        Microsoft.Extensions.Logging.ILogger<ProcessingWatchedFolderWatcherService> logger)
        : ProcessingWatchedFolderWatcherService(database, options, jobStore, state, time, logger)
    {
        private readonly ConcurrentDictionary<string, List<FakeFileSystemWatcher>> _created = new(StringComparer.Ordinal);

        public List<FakeFileSystemWatcher> CreatedFor(string folder) =>
            _created.TryGetValue(Path.GetFullPath(folder), out var list) ? list.ToList() : [];

        internal override FileSystemWatcher CreateFileSystemWatcher(string folder)
        {
            var fake = new FakeFileSystemWatcher(folder);
            _created.AddOrUpdate(
                Path.GetFullPath(folder),
                _ => [fake],
                (_, existing) =>
                {
                    lock (existing)
                    {
                        existing.Add(fake);
                        return existing;
                    }
                });
            return fake;
        }
    }
}
