using Weir.Core.Jobs;
using Weir.Infrastructure.Activity;

namespace Weir.Infrastructure.Tests.MediaManagers;

/// <summary>Cancelling a queued job from System › Logs is announced, so every open list shows it cancelled.</summary>
public sealed class PendingJobCancellationTests : IDisposable
{
    private readonly MediaManagerFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private async Task<string[]> HeardAsync(BroadcastSubscription<string> heard)
    {
        heard.Dispose();
        var topics = new List<string>();
        await foreach (var topic in heard.ReadAllAsync(CancellationToken.None))
        {
            topics.Add(topic);
        }

        return [.. topics];
    }

    [Fact]
    public async Task A_cancelled_job_is_announced_once_it_is_committed()
    {
        var job = await _fixture.Jobs.EnqueueOrGetAsync("cancel-me", "processing.test.harness.v1");
        using var heard = _fixture.Changes.Subscribe();

        var result = await _fixture.Db(uow => _fixture.Cancellation.CancelAsync(uow, job.Id));

        Assert.Equal(JobActionOutcome.Ok, result.Outcome);
        Assert.Equal([DataTopics.Jobs, DataTopics.FilesAtOnce], await HeardAsync(heard));
    }

    [Fact]
    public async Task A_cancel_that_is_not_committed_says_nothing()
    {
        var job = await _fixture.Jobs.EnqueueOrGetAsync("cancel-me", "processing.test.harness.v1");
        using var heard = _fixture.Changes.Subscribe();

        await _fixture.Db(uow => _fixture.Cancellation.CancelAsync(uow, job.Id), commit: false);

        Assert.Empty(await HeardAsync(heard));
    }

    [Fact]
    public async Task A_job_that_cannot_be_cancelled_says_nothing()
    {
        using var heard = _fixture.Changes.Subscribe();

        var result = await _fixture.Db(uow => _fixture.Cancellation.CancelAsync(uow, 12345));

        Assert.Equal(JobActionOutcome.NotFound, result.Outcome);
        Assert.Empty(await HeardAsync(heard));
    }
}
