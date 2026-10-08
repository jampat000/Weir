using Microsoft.Playwright;
using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;

namespace Weir.E2E.Tests.Harness;

/// <summary>
/// The one server and the one Playwright driver every browser test shares (<c>ICollectionFixture</c>), for the
/// whole run. The server is the real built host serving the built web
/// app itself, exactly as the Docker and Windows packages do. Without <c>WEIR_E2E=1</c> nothing starts.
/// </summary>
public sealed class E2EServer : IAsyncLifetime
{
    private WeirServer? _server;
    private IPlaywright? _playwright;

    public IPlaywright Playwright => _playwright ?? throw NotStarted();

    public string DatabasePath => (_server ?? throw NotStarted()).DatabasePath;

    public string Home => (_server ?? throw NotStarted()).Home;

    /// <summary>The server's address without a trailing slash.</summary>
    public string BaseUrl => (_server ?? throw NotStarted()).BaseUrl.GetLeftPart(UriPartial.Authority);

    /// <summary>
    /// A server of its own, set up as the Windows package is (the About screen offers an update to install only there), for a
    /// test that has to stop and start it: the shared server serves every other test and is left running.
    /// </summary>
    public static Task<WeirServer> StartWindowsInstallAsync() =>
        WeirServer.StartNewAsync(Environment.With(("WEIR_RUNTIME", "windows")));

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

    // The server runs as an operator's does: the workers, the file watcher, the periodic scan and the work file
    // sweeps are on (without the sweeps the log has no job rows for the Jobs source to show).
    private static Dictionary<string, string> Environment =>
        ServerEnvironment.OperatorDefaults.With(("WEIR_WEB_DIST", RepoPaths.WebDist));

    private static InvalidOperationException NotStarted() => new("The E2E server has not started: set WEIR_E2E=1.");
}
