using Microsoft.Extensions.Logging.Abstractions;
using Weir.Core.Jobs;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.Jobs;

/// <summary>
/// The work file sweep removes Weir's own left-behind temp output while Weir runs. It was queued every hour with no
/// handler, so it never ran and such files were only cleared at the next restart.
/// </summary>
public sealed class WorkTempStaleSweepHandlerTests : IDisposable
{
    private readonly StoreFixture _store = new();
    private readonly WorkTempStaleSweepHandler _handler;
    private readonly string _movieWork;
    private readonly string _tvWork;

    public WorkTempStaleSweepHandlerTests()
    {
        _store.Clock.Set(DateTimeOffset.UtcNow);
        _handler = new WorkTempStaleSweepHandler(
            new ProcessingJobStore(_store.Database, _store.Clock), _store.Options, _store.Clock, NullLogger<WorkTempStaleSweepHandler>.Instance);
        _movieWork = ProcessingLibraryFolders.DefaultMovieWorkFolder(_store.Options.WeirHome);
        _tvWork = ProcessingLibraryFolders.DefaultTvWorkFolder(_store.Options.WeirHome);
        Directory.CreateDirectory(_movieWork);
        Directory.CreateDirectory(_tvWork);
    }

    public void Dispose() => _store.Dispose();

    private static string File(string folder, string name, TimeSpan age)
    {
        var path = Path.Join(folder, name);
        System.IO.File.WriteAllText(path, "partial remux output");
        System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        return path;
    }

    private Task RunAsync(string scope) =>
        _handler.HandleAsync(
            new JobWorkContext(1, PeriodicJobKinds.WorkTempStaleSweep, $"{{\"media_scope\": \"{scope}\", \"trigger\": \"scheduled\"}}", "test"),
            CancellationToken.None);

    [Fact]
    public async Task Removes_weirs_own_temp_output_once_it_is_older_than_the_stale_age()
    {
        var stale = File(_movieWork, "Paper.Lanterns.2023.processing.ab12cd34.mkv", TimeSpan.FromDays(2));
        var fresh = File(_movieWork, "The.Long.Tide.2024.processing.ef56gh78.mkv", TimeSpan.FromMinutes(5));

        await RunAsync("movie");

        Assert.False(System.IO.File.Exists(stale));
        Assert.True(System.IO.File.Exists(fresh));
        Assert.Equal(1, await _store.Scalar(
            "SELECT count(*) FROM activity_events WHERE event_type = 'processing.work_temp_stale_sweep_completed' AND detail LIKE '%\"files_removed\":1%'"));
    }

    [Fact]
    public async Task Never_touches_a_file_that_is_not_weirs_however_old()
    {
        var notOurs = File(_movieWork, "Paper.Lanterns.2023.mkv", TimeSpan.FromDays(30));
        var lookalike = File(_movieWork, "notes.processing.txt", TimeSpan.FromDays(30));

        await RunAsync("movie");

        Assert.True(System.IO.File.Exists(notOurs));
        Assert.True(System.IO.File.Exists(lookalike));
    }

    [Fact]
    public async Task Leaves_the_temp_output_of_a_pass_that_is_running_now()
    {
        var running = File(_movieWork, "Paper.Lanterns.2023.processing.ab12cd34.mkv", TimeSpan.FromDays(2));
        var job = await new ProcessingJobStore(_store.Database, _store.Clock).EnqueueOrGetAsync(
            "remux:paper-lanterns", "processing.file.remux_pass.v1", "{\"relative_media_path\": \"Paper Lanterns (2023)/Paper.Lanterns.2023.mkv\", \"media_scope\": \"movie\"}");
        await _store.Execute($"UPDATE jobs SET status = 'leased', lease_owner = 'worker' WHERE id = {job.Id}");

        await RunAsync("movie");

        Assert.True(System.IO.File.Exists(running));
    }

    [Fact]
    public async Task Sweeps_only_its_own_scope()
    {
        var movie = File(_movieWork, "Paper.Lanterns.2023.processing.ab12cd34.mkv", TimeSpan.FromDays(2));
        var tv = File(_tvWork, "Northbound.S01E02.processing.ij90kl12.mkv", TimeSpan.FromDays(2));

        await RunAsync("tv");

        Assert.True(System.IO.File.Exists(movie));
        Assert.False(System.IO.File.Exists(tv));
    }
}
