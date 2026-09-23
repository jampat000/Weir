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
    private readonly NotificationDispatcher _dispatcher;
    private readonly WebhookJobNotifications _notifications;

    public WebhookJobNotificationsTests()
    {
        _dispatcher = new NotificationDispatcher(_poster, _store.Clock);
        _notifications = new WebhookJobNotifications(_store.Database, _dispatcher, NullLogger<WebhookJobNotifications>.Instance);
    }

    /// <summary>
    /// Everything posted once the background deliveries have finished. Waiting on the deliveries themselves, not on a
    /// clock, is what keeps these tests steady on a slow runner (#669).
    /// </summary>
    private async Task<List<(string Url, string Body)>> PostsAsync()
    {
        await _dispatcher.WhenIdleAsync();
        return _poster.Posts;
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

        var posts = await PostsAsync();
        Assert.Equal(["https://alerts.example.com/any", "https://alerts.example.com/files"], posts.Select(p => p.Url).Order());
        Assert.All(posts, p => Assert.Contains("\"processing_job_completed\"", p.Body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_library_clean_is_file_processing_too()
    {
        await ChannelAsync("files", "processing_job_failed");
        var job = await JobAsync(LibraryClean, "failed");

        _notifications.Dispatch("processing", "failed", job, LibraryClean);

        var post = Assert.Single(await PostsAsync());
        Assert.Equal("https://alerts.example.com/files", post.Url);
    }

    [Fact]
    public async Task A_scan_finishing_sends_nothing_even_to_any_job_channels()
    {
        await ChannelAsync("any", "job_completed", "processing_job_completed");
        var job = await JobAsync(FolderScan, "completed");

        _notifications.Dispatch("processing", "completed", job, FolderScan);

        Assert.Empty(await PostsAsync());
    }

    [Fact]
    public async Task A_scan_that_failed_for_good_reaches_any_job_failed_but_not_file_processing()
    {
        await ChannelAsync("files", "processing_job_failed");
        await ChannelAsync("failures", "job_failed");
        var job = await JobAsync(FolderScan, "failed");

        _notifications.Dispatch("processing", "failed", job, FolderScan);

        var post = Assert.Single(await PostsAsync());
        Assert.Equal("https://alerts.example.com/failures", post.Url);
    }

    [Fact]
    public async Task A_failure_with_a_retry_still_coming_sends_nothing()
    {
        await ChannelAsync("failures", "job_failed", "processing_job_failed");
        var job = await JobAsync(RemuxPass, "pending");

        _notifications.Dispatch("processing", "failed", job, RemuxPass, willRetry: true);

        Assert.Empty(await PostsAsync());
    }

    /// <summary>Records every post instead of sending it.</summary>
    private sealed class RecordingPoster : IExternalJsonPoster
    {
        private readonly ConcurrentQueue<(string Url, string Body)> _posts = new();

        public List<(string Url, string Body)> Posts => [.. _posts];

        public Task<int> PostJsonAsync(string url, byte[] body, IReadOnlyDictionary<string, string> headers, TimeSpan timeout, CancellationToken cancellationToken)
        {
            _posts.Enqueue((url, Encoding.UTF8.GetString(body)));
            return Task.FromResult(204);
        }
    }
}
