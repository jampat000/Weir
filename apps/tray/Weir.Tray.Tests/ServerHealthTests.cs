using System.Net;
using System.Threading.Channels;

using Microsoft.Extensions.Time.Testing;

using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// The start-up wait for a server's /ready: it pauses between attempts, frees every response, and ends in a
/// <see cref="TimeoutException"/> or an exit error, never a network exception. Time is fake and every step waits on
/// a signal, so nothing here depends on the machine's speed.
/// </summary>
public sealed class ServerHealthTests
{
    private const int TestTimeoutMs = 10_000;
    private static readonly Uri ReadyUrl = new("http://127.0.0.1:9347/ready");
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(250);

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Each_retry_waits_the_pause_before_asking_again()
    {
        var clock = new DelayWatchingTimeProvider();
        var handler = new ScriptedHandler(call => call < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        var waiting = Wait(client, clock, TimeSpan.FromSeconds(60));
        await handler.NextCall();
        Assert.Equal(Pause, await clock.NextDelay());
        Assert.Equal(1, handler.Calls);
        clock.Advance(Pause);
        await handler.NextCall();
        Assert.Equal(Pause, await clock.NextDelay());
        clock.Advance(Pause);
        await waiting;

        Assert.Equal(3, handler.Calls);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Every_response_is_freed()
    {
        var clock = new DelayWatchingTimeProvider();
        var handler = new ScriptedHandler(call => call < 2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        var waiting = Wait(client, clock, TimeSpan.FromSeconds(60));
        await clock.NextDelay();
        clock.Advance(Pause);
        await waiting;

        Assert.All(handler.Responses, response => Assert.True(response.Disposed));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task A_server_that_never_gets_ready_times_out_after_the_timeout()
    {
        var clock = new DelayWatchingTimeProvider();
        var handler = new ScriptedHandler(_ => HttpStatusCode.ServiceUnavailable);
        using var client = new HttpClient(handler);

        var waiting = Wait(client, clock, Pause * 2);
        await DriveUntilDone(waiting, clock);

        var error = await Assert.ThrowsAsync<TimeoutException>(() => waiting);
        Assert.Contains("HTTP 503", error.Message);
        Assert.Equal(3, handler.Calls);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Nothing_listening_ends_in_a_timeout_not_a_network_error()
    {
        var clock = new DelayWatchingTimeProvider();
        var handler = new ScriptedHandler(_ => throw new HttpRequestException("No connection could be made"));
        using var client = new HttpClient(handler);

        var waiting = Wait(client, clock, Pause);
        await DriveUntilDone(waiting, clock);

        var error = await Assert.ThrowsAsync<TimeoutException>(() => waiting);
        Assert.Contains("No connection could be made", error.Message);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task A_server_that_exited_fails_at_once_with_its_exit_code()
    {
        var handler = new ScriptedHandler(_ => HttpStatusCode.ServiceUnavailable);
        using var client = new HttpClient(handler);
        var timing = new ServerHealth.Timing(TimeSpan.FromSeconds(60), Pause, new FakeTimeProvider());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ServerHealth.WaitUntilReadyAsync(client, ReadyUrl, () => 3, timing, CancellationToken.None));

        Assert.Contains("code 3", error.Message);
        Assert.Equal(0, handler.Calls);
    }

    private static Task Wait(HttpClient client, TimeProvider clock, TimeSpan timeout) =>
        ServerHealth.WaitUntilReadyAsync(client, ReadyUrl, () => null, new ServerHealth.Timing(timeout, Pause, clock), CancellationToken.None);

    // Lets each pause pass as soon as the wait starts it, until the wait ends.
    private static async Task DriveUntilDone(Task waiting, DelayWatchingTimeProvider clock)
    {
        while (await Task.WhenAny(waiting, clock.NextDelay()) != waiting)
        {
            clock.Advance(Pause);
        }
    }

    /// <summary>Fake time that reports each delay the code under test starts.</summary>
    private sealed class DelayWatchingTimeProvider : FakeTimeProvider
    {
        private readonly Channel<TimeSpan> _delays = Channel.CreateUnbounded<TimeSpan>();

        public Task<TimeSpan> NextDelay() => _delays.Reader.ReadAsync().AsTask();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            _delays.Writer.TryWrite(dueTime);
            return timer;
        }
    }

    private sealed class ScriptedHandler(Func<int, HttpStatusCode> answer) : HttpMessageHandler
    {
        private readonly Channel<int> _calls = Channel.CreateUnbounded<int>();
        private int _count;

        public int Calls => Volatile.Read(ref _count);

        public List<TrackedResponse> Responses { get; } = [];

        public Task<int> NextCall() => _calls.Reader.ReadAsync().AsTask();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _count);
            _calls.Writer.TryWrite(call);
            var response = new TrackedResponse(answer(call));
            lock (Responses)
            {
                Responses.Add(response);
            }
            return Task.FromResult<HttpResponseMessage>(response);
        }
    }

    private sealed class TrackedResponse(HttpStatusCode status) : HttpResponseMessage(status)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
