using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Weir.Api.Tests.MediaManagers;
using Weir.Api.Tests.Platform;
using Weir.Infrastructure.MediaManagers;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.Processing;

/// <summary>
/// A media manager that does not answer while first-run setup asks it for folders is an expected condition: the setup
/// screen is told so in plain words, and the server log carries no error for it.
/// </summary>
public sealed class LibrarySuggestionsManagerFailureApiTests
{
    private const string Suggestions = "/api/v1/processing/library-suggestions";
    private const string DelunoManifestPath = "/api/integrations/external/manifest";

    [Fact]
    public async Task A_manager_that_times_out_is_named_in_a_note_and_logs_no_error()
    {
        var logger = new CapturingLoggerFactory();
        var manager = new ScriptedManager();
        await using var server = await WeirTestServer.StartAsync(
            [("WEIR_SESSION_SECRET", Secret), ("WEIR_PROCESSING_WORKER_COUNT", "0")],
            configureServices: services => services
                .AddSingleton<IManagerHttpHandlerFactory>(manager)
                .AddSingleton<ILoggerFactory>(logger));
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        using var connected = await client.PostAsync("/api/v1/media-managers/connections", new Dictionary<string, object?>
        {
            ["csrf_token"] = await client.CsrfAsync(),
            ["kind"] = "deluno",
            ["base_url"] = "http://192.0.2.10:5099",
            ["api_key"] = "key",
        });
        Assert.Equal(HttpStatusCode.Created, connected.StatusCode);
        manager.Route(HttpMethod.Get, DelunoManifestPath, _ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException()));

        using var response = await client.GetAsync(Suggestions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var suggested = await Json(response);
        Assert.Empty(suggested["libraries"]!.AsArray());
        Assert.Contains("Deluno on 192.0.2.10", Assert.Single(suggested["notes"]!.AsArray())!.GetValue<string>(), StringComparison.Ordinal);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Error);
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
