using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using Weir.Core.Json;
using Weir.Infrastructure.Processing.RemuxPass;

namespace Weir.Infrastructure.Tests.Processing.RemuxPass;

/// <summary>
/// How a pass's live progress reaches the database (#710): off the tool's output reader, at most once every two seconds,
/// with a change of status saved at once.
/// </summary>
public sealed class ActivityProgressReporterTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly Channel<PyDict> _saved = Channel.CreateUnbounded<PyDict>();

    private ActivityProgressReporter Reporter(Func<PyDict, Task>? save = null) =>
        new(save ?? (body => _saved.Writer.WriteAsync(body).AsTask()), jobId: 7, new PyDict().Set("trigger", "scan"), _time);

    private static PyDict Writing(double percent) => new PyDict().Set("status", "processing").Set("percent", percent);

    private static double Percent(PyDict body) => ((PyFloat)body.Get("percent")!).Value;

    [Fact]
    public async Task The_first_report_is_saved_at_once_with_the_job_and_its_provenance()
    {
        var reporter = Reporter();

        reporter.Report(Writing(10));

        var saved = await _saved.Reader.ReadAsync();
        Assert.Equal(7L, (long)((PyInt)saved.Get("job_id")!).Value);
        Assert.Equal("scan", ((PyStr)saved.Get("trigger")!).Value);
        Assert.Equal(10, Percent(saved));
    }

    [Fact]
    public async Task Reports_within_the_interval_wait_for_it_and_only_the_newest_is_saved()
    {
        var savedAt = new List<DateTimeOffset>();
        var reporter = Reporter(body =>
        {
            savedAt.Add(_time.GetUtcNow());
            return _saved.Writer.WriteAsync(body).AsTask();
        });
        reporter.Report(Writing(10));
        await _saved.Reader.ReadAsync();

        reporter.Report(Writing(20));
        reporter.Report(Writing(30));
        var next = _saved.Reader.ReadAsync().AsTask();
        while (!next.IsCompleted)
        {
            _time.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Yield();
        }

        Assert.Equal(30, Percent(await next));
        Assert.True(savedAt[1] - savedAt[0] >= ActivityProgressReporter.MinimumInterval);
        Assert.False(_saved.Reader.TryRead(out _));
    }

    [Fact]
    public async Task A_change_of_status_is_saved_without_waiting_out_the_interval()
    {
        var reporter = Reporter();
        reporter.Report(Writing(99));
        await _saved.Reader.ReadAsync();

        reporter.Report(new PyDict().Set("status", "finishing").Set("percent", 100.0));

        var saved = await _saved.Reader.ReadAsync();
        Assert.Equal("finishing", ((PyStr)saved.Get("status")!).Value);
    }

    [Fact]
    public async Task Completing_saves_the_newest_report_and_ignores_any_after_it()
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
    public async Task Reports_made_while_a_save_is_running_are_folded_into_the_next_save()
    {
        var firstSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveCanFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new List<string>();
        var reporter = Reporter(async body =>
        {
            lock (saved)
            {
                saved.Add(((PyStr)body.Get("status")!).Value);
            }

            firstSaveStarted.TrySetResult();
            await saveCanFinish.Task;
        });
        reporter.Report(Writing(10));
        await firstSaveStarted.Task;

        reporter.Report(new PyDict().Set("status", "finishing"));
        reporter.Report(new PyDict().Set("status", "finished"));
        saveCanFinish.SetResult();
        await reporter.CompleteAsync();

        Assert.Equal(["processing", "finished"], saved);
    }
}
