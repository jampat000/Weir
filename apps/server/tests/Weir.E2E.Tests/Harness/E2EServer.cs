using Microsoft.Playwright;
using Weir.Contract.Tests.Harness;

namespace Weir.E2E.Tests.Harness;

/// <summary>
/// The one server and the one Playwright driver every browser test shares (<c>ICollectionFixture</c>), as the
/// Python suite shared one server for the whole session. The server is the real built host serving the built web
/// app itself, exactly as the Docker and Windows packages do. Without <c>WEIR_E2E=1</c> nothing starts.
/// </summary>
public sealed class E2EServer : IAsyncLifetime
{
    private const string ProductionWorkerCount = "10";

    private WeirServer? _server;
    private IPlaywright? _playwright;

    public IPlaywright Playwright => _playwright ?? throw NotStarted();

    public string DatabasePath => (_server ?? throw NotStarted()).DatabasePath;

    public string Home => (_server ?? throw NotStarted()).Home;

    /// <summary>The server's address without a trailing slash.</summary>
    public string BaseUrl => (_server ?? throw NotStarted()).BaseUrl.GetLeftPart(UriPartial.Authority);

    public async Task InitializeAsync()
    {
        if (!E2EFactAttribute.IsEnabled)
        {
            return;
        }

        if (!File.Exists(Path.Combine(RepoPaths.WebDist, "index.html")))
        {
            throw new InvalidOperationException("Weir E2E needs the built web app: run `npm ci && npm run build` in apps/web first.");
        }

        _server = await WeirServer.StartNewAsync(Environment);
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        _playwright?.Dispose();
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }

    // The Python suite ran the server as an operator does, so the workers, the file watcher, the periodic scan and the
    // work file sweeps are on, as they are by default (the contract harness's quiet defaults switch them off). Without
    // the sweeps the log has no job rows for the Jobs source to show.
    private static Dictionary<string, string> Environment => new()
    {
        ["WEIR_WEB_DIST"] = RepoPaths.WebDist,
        ["WEIR_PROCESSING_WORKER_COUNT"] = ProductionWorkerCount,
        ["WEIR_PROCESSING_WATCHER_ENABLED"] = "1",
        ["WEIR_PROCESSING_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_PERIODIC_ENQUEUE_REMUX_JOBS"] = "1",
        ["WEIR_PROCESSING_WORK_TEMP_STALE_SWEEP_MOVIE_SCHEDULE_ENABLED"] = "1",
        ["WEIR_PROCESSING_WORK_TEMP_STALE_SWEEP_TV_SCHEDULE_ENABLED"] = "1",
    };

    private static InvalidOperationException NotStarted() => new("The E2E server has not started: set WEIR_E2E=1.");
}
