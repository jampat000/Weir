using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Jobs;

/// <summary>The job queue tells the open screens when a job row changed, once the change is committed, and says nothing when a transaction only read.</summary>
public sealed class JobChangePublishTests : IDisposable
{
    private const string Kind = "processing.test.harness.v1";
    private static readonly DateTimeOffset T0 = JobsTestDatabase.T0;

    private readonly JobsTestDatabase _db = new();
    private readonly DataChangePublisher _changes = new();
    private readonly BroadcastSubscription<string> _heard;
    private readonly ProcessingJobStore _store;

    public JobChangePublishTests()
    {
        _store = new ProcessingJobStore(_db.Database, _db.Clock, changes: _changes);
        _heard = _changes.Subscribe();
    }

    public void Dispose()
    {
        _heard.Dispose();
        _db.Dispose();
    }

    /// <summary>What has been published so far: the stream is completed so that reading it ends.</summary>
    private async Task<string[]> HeardAsync()
    {
        _heard.Dispose();
        var topics = new List<string>();
        await foreach (var topic in _heard.ReadAllAsync(CancellationToken.None))
        {
            topics.Add(topic);
        }

        return [.. topics];
    }

    [Fact]
    public async Task A_job_that_is_queued_is_announced()
    {
        await _store.EnqueueOrGetAsync("k1", Kind);

        Assert.Equal([DataTopics.Jobs], await HeardAsync());
    }

    [Fact]
    public async Task A_job_that_is_already_queued_changes_nothing_and_says_nothing()
    {
        await _store.EnqueueOrGetAsync("k1", Kind);
        await _store.EnqueueOrGetAsync("k1", Kind);

        Assert.Equal([DataTopics.Jobs], await HeardAsync());
    }

    [Fact]
    public async Task Claiming_finishing_and_failing_a_job_are_each_announced()
    {
        var first = await _store.EnqueueOrGetAsync("k1", Kind);
        var second = await _store.EnqueueOrGetAsync("k2", Kind, maxAttempts: 1);

        await _store.ClaimNextAsync("w", T0.AddHours(1), T0);
        await _store.CompleteClaimedAsync(first.Id, "w", T0);
        await _store.ClaimNextAsync("w", T0.AddHours(1), T0);
        await _store.FailClaimedAsync(second.Id, "w", "It went wrong.", T0);

        Assert.Equal(Enumerable.Repeat(DataTopics.Jobs, 6), await HeardAsync());
    }

    [Fact]
    public async Task A_claim_that_finds_nothing_says_nothing()
    {
        Assert.Null(await _store.ClaimNextAsync("w", T0.AddHours(1), T0));

        Assert.Empty(await HeardAsync());
    }

    private async Task QueueOnAUnitOfWorkAsync(bool commit)
    {
        var uow = await UnitOfWork.OpenAsync(_db.Database);
        await using (uow)
        {
            _store.EnqueueOrGet(uow, "u1", Kind, null, maxAttempts: 3, runnerCost: null, priority: 0);
            _store.EnqueueOrGet(uow, "u2", Kind, null, maxAttempts: 3, runnerCost: null, priority: 0);
            if (commit)
            {
                await uow.CommitAsync();
            }
        }
    }

    [Fact]
    public async Task Jobs_queued_on_a_unit_of_work_are_announced_once_when_it_commits()
    {
        await QueueOnAUnitOfWorkAsync(commit: true);

        Assert.Equal([DataTopics.Jobs], await HeardAsync());
    }

    [Fact]
    public async Task Jobs_queued_on_a_unit_of_work_that_is_not_committed_say_nothing()
    {
        await QueueOnAUnitOfWorkAsync(commit: false);

        Assert.Empty(await HeardAsync());
    }

    [Fact]
    public async Task A_change_that_is_rolled_back_says_nothing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.InTransactionAsync<int>(
            (connection, transaction) =>
            {
                ProcessingJobStore.Execute(connection, transaction, "INSERT INTO jobs (dedupe_key, job_kind, status) VALUES ('k', 'processing.test.v1', 'pending')");
                throw new InvalidOperationException("The work failed.");
            }));

        Assert.Empty(await HeardAsync());
    }
}
