using Microsoft.Playwright;
using Weir.E2E.Tests.Harness;

namespace Weir.E2E.Tests.Support;

/// <summary>
/// A browser test: it starts from the setup page (the previous test's users and settings are cleared), gets a
/// headless Chromium of its own, and closes it afterwards.
/// </summary>
[Collection(SharedServer.Name)]
public abstract class E2ETestBase(E2EServer server) : IAsyncLifetime
{
    private const float DefaultTimeoutMs = 30_000;

    private IBrowser? _browser;

    protected E2EServer Server { get; } = server;

    protected string BaseUrl => Server.BaseUrl;

    public async Task InitializeAsync()
    {
        E2EDatabase.ResetPerTestState(Server.DatabasePath);
        _browser = await Server.Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }
    }

    /// <summary>A page in a context of its own, at Playwright's 1280x720 unless a viewport is given.</summary>
    protected async Task<IPage> NewPageAsync(ViewportSize? viewport = null)
    {
        var page = await _browser!.NewPageAsync(new BrowserNewPageOptions { ViewportSize = viewport });
        page.SetDefaultTimeout(DefaultTimeoutMs);
        return page;
    }

    /// <summary>A browser context of its own, for a test that opens several pages in it.</summary>
    protected async Task<IBrowserContext> NewContextAsync(ViewportSize viewport) =>
        await _browser!.NewContextAsync(new BrowserNewContextOptions { ViewportSize = viewport });

    protected static void ApplyDefaultTimeout(IPage page) => page.SetDefaultTimeout(DefaultTimeoutMs);
}
