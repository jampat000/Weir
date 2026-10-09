using Weir.Infrastructure.Runtime;

namespace Weir.Infrastructure.Tests.Runtime;

/// <summary>Long blocking calls run on threads of their own, so the thread pool stays free for requests and the live stream.</summary>
public sealed class BlockingWorkTests
{
    [Fact]
    public async Task The_work_runs_on_a_thread_of_its_own()
    {
        Assert.False(await BlockingWork.RunAsync(() => Thread.CurrentThread.IsThreadPoolThread));
    }

    [Fact]
    public async Task A_result_and_a_failure_come_back_to_the_caller()
    {
        Assert.Equal(7, await BlockingWork.RunAsync(() => 7));
        await Assert.ThrowsAsync<InvalidOperationException>(() => BlockingWork.RunAsync(() => throw new InvalidOperationException("copy failed")));
    }

    [Fact]
    public async Task Pool_threads_stay_free_while_many_blocking_calls_are_in_flight()
    {
        // More than the pool's minimum, which is what turns blocked pool threads into seconds of delay for everything else.
        var blocked = Environment.ProcessorCount * 4 + 8;
        using var release = new ManualResetEventSlim();
        var running = Enumerable.Range(0, blocked)
            .Select(_ => BlockingWork.RunAsync(() => release.Wait(TimeSpan.FromSeconds(60))))
            .ToArray();
        try
        {
            Assert.Equal(42, await Task.Run(() => 42).WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            release.Set();
            await Task.WhenAll(running);
        }
    }
}
