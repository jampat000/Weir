using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Weir.Api.Tests.Platform;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.MediaManagers;

/// <summary>
/// A hand-off that no workflow's watched folder contains is refused with the workflows listed, and every refused
/// hand-off is written to the server log, because the media manager that sent it may show nobody the answer.
/// </summary>
public sealed class RefusedHandoffApiTests
{
    private const string WebhookSecret = "s3cret";

    private static readonly Dictionary<string, string> SecretHeader = new() { ["X-Webhook-Secret"] = WebhookSecret };

    private static Task<WeirTestServer> StartAsync(ILoggerFactory logger) =>
        WeirTestServer.StartAsync(
            [("WEIR_SESSION_SECRET", Secret), ("WEIR_PROCESSING_WORKER_COUNT", "0"), ("WEIR_MEDIA_MANAGER_WEBHOOK_SECRET", WebhookSecret)],
            configureServices: services => services.AddSingleton<ILoggerFactory>(logger));

    private static Dictionary<string, object> Handoff(string source) => new()
    {
        ["eventType"] = "deluno.processor-handoff",
        ["handoffId"] = "h1",
        ["libraryId"] = "lib-1",
        ["mediaType"] = "movies",
        ["sourcePath"] = source,
        ["releaseName"] = "Nosferatu.1922.1080p.BluRay-PublicHD",
        ["callbackPath"] = "/api/integrations/processors/events",
    };

    [Fact]
    public async Task A_file_no_workflow_watches_is_refused_listing_the_workflows_and_is_logged()
    {
        var logger = new CapturingLoggerFactory();
        await using var server = await StartAsync(logger);
        await TestDatabase.ExecuteAsync(server, "UPDATE libraries SET watched_folder = $w WHERE media_type = 'movie'", ("$w", @"C:\Downloads\Completed\Movies"));
        await TestDatabase.ExecuteAsync(server, "UPDATE libraries SET watched_folder = '' WHERE media_type = 'tv'");

        using var response = await new ApiTestClient(server).PostAsync(
            "/api/v1/intake/webhook/deluno", Handoff(@"D:\Elsewhere\Completed\Nosferatu.1922\Nosferatu.mkv"), SecretHeader);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var detail = await Detail(response);
        Assert.Equal(
            "No Weir workflow watches the folder that 'Nosferatu.mkv' is in. " +
            @"Weir's workflows: Movies watches 'C:\Downloads\Completed\Movies'; TV (TV episodes) has no watched folder yet. " +
            "Set a workflow's watched folder (Setup › Workflows) to the folder the download client finishes into, or add a path mapping in the media manager.",
            detail);
        Assert.DoesNotContain("Elsewhere", detail, StringComparison.Ordinal);

        var warning = Assert.Single(logger.Entries, entry => entry.Message.StartsWith("Hand-off from deluno refused", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("status 400", warning.Message, StringComparison.Ordinal);
        Assert.Contains(detail, warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(WebhookSecret, warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hand_off_that_is_accepted_logs_no_refusal()
    {
        var logger = new CapturingLoggerFactory();
        await using var server = await StartAsync(logger);
        var watched = Path.Join(Path.GetTempPath(), "weir-refused-" + Guid.NewGuid().ToString("N"));
        var source = Path.Join(watched, "Film", "film.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        try
        {
            await File.WriteAllTextAsync(source, "the original download");
            await TestDatabase.ExecuteAsync(server, "UPDATE libraries SET watched_folder = $w WHERE media_type = 'movie'", ("$w", watched));

            using var response = await new ApiTestClient(server).PostAsync("/api/v1/intake/webhook/deluno", Handoff(source), SecretHeader);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain(logger.Entries, entry => entry.Message.Contains("refused", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(watched, recursive: true);
        }
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        // Background timers log through this factory too, from their own threads.
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerFactory owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
