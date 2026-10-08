using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.Http;

/// <summary>What a start with the settings the tray and the Docker image provide logs: nothing a person has to act on.</summary>
public sealed class StartupSettingWarningsTests
{
    private const string Credentials = "startup-warning-tests-credentials-secret-0123456789";

    [Fact]
    public async Task A_default_start_warns_about_no_unset_setting()
    {
        var logger = new CapturingLoggerFactory();
        await using var server = await WeirTestServer.StartAsync(
            [("WEIR_SESSION_SECRET", Secret), ("WEIR_CREDENTIALS_SECRET", Credentials), ("WEIR_PROCESSING_WORKER_COUNT", "0")],
            configureServices: services => services.AddSingleton<ILoggerFactory>(logger));

        var startup = logger.Entries.Where(entry => entry.Category == "Weir.Host.Startup").ToList();
        Assert.NotEmpty(startup);
        Assert.DoesNotContain(startup, entry => entry.Level >= LogLevel.Warning && entry.Message.Contains("WEIR_", StringComparison.Ordinal));
    }

    /// <summary>A logger that keeps every message with its category, so a test can assert on what start-up wrote.</summary>
    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        // Background timers log through this factory too, from their own threads.
        public ConcurrentQueue<(string Category, LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerFactory owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Entries.Enqueue((category, logLevel, formatter(state, exception)));
        }
    }
}
