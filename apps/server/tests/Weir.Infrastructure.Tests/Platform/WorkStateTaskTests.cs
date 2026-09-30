using System.Text.Json;
using Weir.Core.Jobs;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Runtime;
using Weir.Infrastructure.Tests.Jobs;

namespace Weir.Infrastructure.Tests.Platform;

/// <summary>
/// The answer the tray reads to decide whether Weir is idle enough to install a downloaded update (#875): it is written
/// only while an update waits, says whether file work is running, and carries the time it was checked.
/// </summary>
public sealed class WorkStateTaskTests : IDisposable
{
    private const string Remux = "processing.file.remux_pass.v1";
    private const string UpdateState = "{\"downloaded\": true, \"version\": \"9.9.9\"}";

    private readonly StoreFixture _fixture = new();
    private readonly UpdateFiles _files;
    private readonly ProcessingJobStore _jobs;
    private readonly WorkStateTask _task;

    public WorkStateTaskTests()
    {
        _files = new UpdateFiles(_fixture.Options);
        _jobs = new ProcessingJobStore(_fixture.Database, _fixture.Clock);
        _task = new WorkStateTask(_files, _jobs, new JobHandlerRegistry([new DelegateHandler(Remux, _ => { })]), _fixture.Clock);
    }

    private string WorkStatePath => Path.Join(_fixture.Home.Path, UpdateFiles.WorkStateFileName);

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Nothing_is_written_while_no_update_is_waiting()
    {
        await _task.RunOnceAsync(CancellationToken.None);

        Assert.False(File.Exists(WorkStatePath));
    }

    [Fact]
    public async Task An_idle_Weir_with_an_update_waiting_says_so_and_when_it_looked()
    {
        DownloadAnUpdate();

        await _task.RunOnceAsync(CancellationToken.None);

        var state = ReadWorkState();
        Assert.False(state.GetProperty("busy").GetBoolean());
        Assert.Equal(_fixture.Clock.GetUtcNow(), state.GetProperty("checkedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task A_running_pass_is_reported_as_busy()
    {
        DownloadAnUpdate();
        await _jobs.EnqueueOrGetAsync("pass", Remux, "{\"media_scope\": \"movie\"}");
        Assert.NotNull(await _jobs.ClaimNextAsync("w1", _fixture.Clock.GetUtcNow().AddHours(1), _fixture.Clock.GetUtcNow()));

        await _task.RunOnceAsync(CancellationToken.None);

        Assert.True(ReadWorkState().GetProperty("busy").GetBoolean());
    }

    [Fact]
    public async Task Each_run_refreshes_the_time_it_was_checked()
    {
        DownloadAnUpdate();
        await _task.RunOnceAsync(CancellationToken.None);
        var first = ReadWorkState().GetProperty("checkedAt").GetDateTimeOffset();

        _fixture.Clock.Advance(WorkStateTask.Every);
        await _task.RunOnceAsync(CancellationToken.None);

        Assert.Equal(first + WorkStateTask.Every, ReadWorkState().GetProperty("checkedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Writing_the_answer_leaves_no_scratch_file_behind()
    {
        DownloadAnUpdate();

        await _task.RunOnceAsync(CancellationToken.None);

        Assert.Empty(Directory.GetFiles(_fixture.Home.Path, $".{UpdateFiles.WorkStateFileName}.*.tmp"));
    }

    private void DownloadAnUpdate() => File.WriteAllText(Path.Join(_fixture.Home.Path, UpdateFiles.StateFileName), UpdateState);

    private JsonElement ReadWorkState() => JsonDocument.Parse(File.ReadAllText(WorkStatePath)).RootElement;
}
