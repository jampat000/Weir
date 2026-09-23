using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Weir.Infrastructure.Http;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Notifications;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.Notifications;

/// <summary>
/// Settings › Alerts channels receive real job events, not only "Send test": the registered notifier turns job
/// outcomes into alerts on every saved channel.
/// </summary>
public sealed class WebhookJobNotificationsTests : IDisposable
{
    private const string RemuxPass = "processing.file.remux_pass.v1";
    private const string LibraryClean = "processing.library.clean.v1";
    private const string FolderScan = "processing.watched_folder.remux_scan_dispatch.v1";

    private readonly StoreFixture _store = new();
    private readonly RecordingPoster _poster = new();
    private readonly WebhookJobNotifications _notifications;

    public WebhookJobNotificationsTests()
    {
        _notifications = new WebhookJobNotifications(
            _store.Database, new NotificationDispatcher(_poster, _store.Clock), NullLogger<WebhookJobNotifications>.Instance);
    }

    public void Dispose() => _store.Dispose();

    private Task<Weir.Core.Notifications.NotificationChannelRecord> ChannelAsync(string label, params string[] events) =>
        _store.WithUnitOfWork(uow => NotificationChannelStore.CreateAsync(uow, label, "webhook", $"https://alerts.example.com/{label}", events, enabled: true));

    private async Task<long> JobAsync(string kind, string status)
    {
        var job = await new ProcessingJobStore(_store.Database, _store.Clock).EnqueueOrGetAsync($"{kind}:{Guid.NewGuid():N}", kind, "{}");
        await _store.Execute($"UPDATE jobs SET status = '{status}' WHERE id = {job.Id}");
        return job.Id;
    }

    [Fact]
    public async Task A_file_that_finished_reaches_file_processing_and_any_job_channels()
    {
        await ChannelAsync("files", "processing_job_completed");
        await ChannelAsync("any", "job_completed");
        await ChannelAsync("failures", "job_failed");
        var job = await JobAsync(RemuxPass, "completed");

        _notifications.Dispatch("processing", "completed", job, RemuxPass);

        var posts = await _poster.WaitForAsync(2);
        Assert.Equal(["https://alerts.example.com/any", "https://alerts.example.com/files"], posts.Select(p => p.Url).Order());
        Assert.All(posts, p => Assert.Contains("\"processing_job_completed\"", p.Body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_library_clean_is_file_processing_too()
    {
        await ChannelAsync("files", "processing_job_failed");
        var job = await JobAsync(LibraryClean, "failed");

        _notifications.Dispatch("processing", "failed", job, LibraryClean);

        var post = Assert.Single(await _poster.WaitForAsync(1));
        Assert.Equal("https://alerts.example.com/files", post.Url);
    }

    [Fact]
    public async Task A_scan_finishing_sends_nothing_even_to_any_job_channels()
    {
        await ChannelAsync("any", "job_completed", "processing_job_completed");
        var job = await JobAsync(FolderScan, "completed");

        _notifications.Dispatch("processing", "completed", job, FolderScan);

        Assert.Empty(await _poster.WaitForAsync(1, TimeSpan.FromMilliseconds(400)));
    }

    [Fact]
    public async Task A_scan_that_failed_for_good_reaches_any_job_failed_but_not_file_processing()
    {
        await ChannelAsync("files", "processing_job_failed");
        await ChannelAsync("failures", "job_failed");
        var job = await JobAsync(FolderScan, "failed");

        _notifications.Dispatch("processing", "failed", job, FolderScan);

        var post = Assert.Single(await _poster.WaitForAsync(1));
        Assert.Equal("https://alerts.example.com/failures", post.Url);
    }

    [Fact]
    public async Task A_failure_with_a_retry_still_coming_sends_nothing()
    {
        await ChannelAsync("failures", "job_failed", "processing_job_failed");
        var job = await JobAsync(RemuxPass, "pending");

        _notifications.Dispatch("processing", "failed", job, RemuxPass, willRetry: true);

        Assert.Empty(await _poster.WaitForAsync(1, TimeSpan.FromMilliseconds(400)));
    }

    /// <summary>Records every post instead of sending it, and lets a test wait for the background delivery.</summary>
    private sealed class RecordingPoster : IExternalJsonPoster
    {
        private readonly ConcurrentQueue<(string Url, string Body)> _posts = new();

        public Task<int> PostJsonAsync(string url, byte[] body, IReadOnlyDictionary<string, string> headers, TimeSpan timeout, CancellationToken cancellationToken)
        {
            _posts.Enqueue((url, Encoding.UTF8.GetString(body)));
            return Task.FromResult(204);
        }

        /// <summary>The posts once <paramref name="count"/> have arrived, or whatever arrived by the deadline.</summary>
        public async Task<List<(string Url, string Body)>> WaitForAsync(int count, TimeSpan? within = null)
        {
            var deadline = DateTime.UtcNow + (within ?? TimeSpan.FromSeconds(5));
            while (_posts.Count < count && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            if (within is not null)
            {
                // Waiting for nothing: give a stray delivery the whole window to show up.
                while (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(20);
                }
            }

            return [.. _posts];
        }
    }
}
