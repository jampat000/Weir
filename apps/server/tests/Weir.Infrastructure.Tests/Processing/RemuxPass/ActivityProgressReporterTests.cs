using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using Weir.Core.Json;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Processing.RemuxPass;

namespace Weir.Infrastructure.Tests.Processing.RemuxPass;

/// <summary>
/// How a pass's live progress reaches the database (#750): a row at the start of a pass and when its stage
/// changes, and a final save at the end even when the stage did not just change. Everything in between — a
/// percent-only update inside the same stage, however often ffmpeg reports one — stays in
/// <see cref="LiveProgressStore"/> and never touches the database.
/// </summary>
public sealed class ActivityProgressReporterTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly LiveProgressStore _liveProgress = new();
    private readonly Channel<WireObject> _saved = Channel.CreateUnbounded<WireObject>();

    private ActivityProgressReporter Reporter(Func<WireObject, Task>? save = null) =>
        new(save ?? (body => _saved.Writer.WriteAsync(body).AsTask()), jobId: 7, new WireObject().Set("trigger", "scan").Set("relative_media_path", "Film/Film.mkv"), _time, _liveProgress);

    private static WireObject Writing(double percent) => new WireObject().Set("status", "processing").Set("percent", percent);

    private static double Percent(WireObject body) => ((WireNumber)body.Get("percent")!).Value;

    [Fact]
    public async Task The_first_report_is_saved_at_once_with_the_job_and_its_provenance()
    {
        var reporter = Reporter();

        reporter.Report(Writing(10));

        var saved = await _saved.Reader.ReadAsync();
        Assert.Equal(7L, (long)((WireInteger)saved.Get("job_id")!).Value);
        Assert.Equal("scan", ((WireString)saved.Get("trigger")!).Value);
        Assert.Equal(10, Percent(saved));
    }

    [Fact]
    public async Task A_percent_only_update_inside_the_same_stage_is_never_saved()
    {
        var reporter = Reporter();
        reporter.Report(Writing(10));
        await _saved.Reader.ReadAsync();

        reporter.Report(Writing(20));
        reporter.Report(Writing(30));
        reporter.Report(Writing(40));

        // Nothing further arrives: no database write happened for the three percent-only reports.
        Assert.False(_saved.Reader.TryRead(out _));
    }

    [Fact]
    public void A_percent_only_update_still_reaches_the_live_progress_store()
    {
        var reporter = Reporter();
        reporter.Report(Writing(10));

        reporter.Report(Writing(64));

        Assert.Equal(64, _liveProgress.Snapshot()["Film/Film.mkv"].Percent);
    }

    [Fact]
    public async Task A_change_of_status_is_saved_without_waiting()
    {
        var reporter = Reporter();
        reporter.Report(Writing(99));
        await _saved.Reader.ReadAsync();

        reporter.Report(new WireObject().Set("status", "finishing").Set("percent", 100.0));

        var saved = await _saved.Reader.ReadAsync();
        Assert.Equal("finishing", ((WireString)saved.Get("status")!).Value);
    }

    [Fact]
    public async Task Completing_saves_a_percent_only_update_that_had_not_reached_the_database_yet()
    {
        var reporter = Reporter();
        reporter.Report(Writing(10));
        await _saved.Reader.ReadAsync();
        reporter.Report(Writing(20));

        await reporter.CompleteAsync();
        reporter.Report(Writing(30));

        Assert.True(_saved.Reader.TryRead(out var last));
        Assert.Equal(20, Percent(last));
        Assert.False(_saved.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Completing_right_after_a_stage_change_does_not_save_it_twice()
    {
        var reporter = Reporter();
        reporter.Report(Writing(10));
        await _saved.Reader.ReadAsync();
        reporter.Report(new WireObject().Set("status", "finished").Set("percent", 100.0));
        await _saved.Reader.ReadAsync();

        await reporter.CompleteAsync();

        Assert.False(_saved.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Completing_with_no_report_at_all_saves_nothing()
    {
        var reporter = Reporter();

        await reporter.CompleteAsync();

        Assert.False(_saved.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Reports_made_while_a_save_is_running_are_folded_into_the_next_save()
    {
        var firstSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveCanFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new List<string>();
        var reporter = Reporter(async body =>
        {
            lock (saved)
            {
                saved.Add(((WireString)body.Get("status")!).Value);
            }

            firstSaveStarted.TrySetResult();
            await saveCanFinish.Task;
        });
        reporter.Report(Writing(10));
        await firstSaveStarted.Task;

        reporter.Report(new WireObject().Set("status", "finishing"));
        reporter.Report(new WireObject().Set("status", "finished"));
        saveCanFinish.SetResult();
        await reporter.CompleteAsync();

        Assert.Equal(["processing", "finished"], saved);
    }

    [Fact]
    public async Task The_pass_leaves_the_live_progress_store_once_it_completes()
    {
        var reporter = Reporter();
        reporter.Report(Writing(50));
        Assert.True(_liveProgress.Snapshot().ContainsKey("Film/Film.mkv"));

        await reporter.CompleteAsync();

        Assert.False(_liveProgress.Snapshot().ContainsKey("Film/Film.mkv"));
    }

    [Fact]
    public void A_report_whose_status_is_not_live_never_enters_the_store()
    {
        var reporter = Reporter();

        reporter.Report(new WireObject().Set("status", "waiting"));

        Assert.False(_liveProgress.Snapshot().ContainsKey("Film/Film.mkv"));
    }
}
