using Microsoft.Playwright;
using Weir.E2E.Tests.Harness;
using Weir.E2E.Tests.Support;
using static Microsoft.Playwright.Assertions;

namespace Weir.E2E.Tests;

/// <summary>The screens follow Weir live: they say when the live connection drops, catch up when it is back, and need no reload.</summary>
public sealed class LiveConnectionTests(E2EServer server) : E2ETestBase(server)
{
    // The longest the page may take to notice: the 5 s the stream asks the browser to wait before it tries again.
    private const float ReconnectMs = 20_000;

    /// <summary>Signs in and waits until the page has its live stream open.</summary>
    private async Task<IPage> OpenLiveShellAsync()
    {
        var page = await NewPageAsync();
        await page.RunAndWaitForResponseAsync(
            () => Navigation.EnsureSignedInAsync(page, BaseUrl),
            response => response.Url.EndsWith("/api/v1/activity/stream", StringComparison.Ordinal));
        await Expect(page.GetByTestId("pause-open")).ToBeVisibleAsync();
        await page.EvaluateAsync("() => { window.__weirNotReloaded = true; }");
        return page;
    }

    private static async Task AssertNotReloadedAsync(IPage page) =>
        Assert.True(await page.EvaluateAsync<bool>("() => window.__weirNotReloaded === true"), "The page was reloaded.");

    [E2EFact]
    public async Task The_banner_follows_the_connection_and_the_page_catches_up_without_a_reload()
    {
        var page = await OpenLiveShellAsync();
        var banner = page.GetByTestId("live-connection-banner");
        await Expect(banner).ToHaveCountAsync(0);

        await page.Context.SetOfflineAsync(true);

        await Expect(banner).ToHaveTextAsync("Live updates paused: Weir isn't answering. Reconnecting…", new() { Timeout = ReconnectMs });
        await WeirApi.SetPausedAsync(page.Context, BaseUrl, paused: true);
        await Expect(page.GetByTestId("pause-badge")).ToHaveCountAsync(0);

        await page.Context.SetOfflineAsync(false);

        await Expect(banner).ToHaveCountAsync(0, new() { Timeout = ReconnectMs });
        await Expect(page.GetByTestId("pause-badge")).ToBeVisibleAsync(new() { Timeout = ReconnectMs });
        await Expect(page.GetByTestId("pause-resume")).ToBeVisibleAsync();
        await AssertNotReloadedAsync(page);
    }

    [E2EFact]
    public async Task Pausing_and_resuming_through_the_api_changes_the_header_without_a_reload()
    {
        var page = await OpenLiveShellAsync();

        await WeirApi.SetPausedAsync(page.Context, BaseUrl, paused: true);
        await Expect(page.GetByTestId("pause-badge")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("pause-resume")).ToBeVisibleAsync();

        await WeirApi.SetPausedAsync(page.Context, BaseUrl, paused: false);
        await Expect(page.GetByTestId("pause-badge")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("pause-open")).ToBeVisibleAsync();

        await AssertNotReloadedAsync(page);
    }
}
