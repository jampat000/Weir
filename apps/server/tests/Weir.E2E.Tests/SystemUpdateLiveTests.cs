using Microsoft.Playwright;
using Weir.Contract.Tests.Harness;
using Weir.E2E.Tests.Harness;
using Weir.E2E.Tests.Support;
using static Microsoft.Playwright.Assertions;

namespace Weir.E2E.Tests;

/// <summary>
/// System › About follows the update the tray downloads and the restart that installs it, with no reload. The tray is not
/// started by these tests: it tells the server an update was downloaded by writing a file in Weir's data folder, so the tests
/// write that file (<see cref="TrayFiles"/>), and stopping and starting the server is what the tray does to install. Each test
/// has a server of its own, set up as the Windows package, so restarting it disturbs no other test.
/// </summary>
public sealed class SystemUpdateLiveTests(E2EServer server) : E2ETestBase(server)
{
    private const float NoticeMs = 10_000;
    private const float RecoverMs = 40_000;

    private const string ReadyNotice = "Update ready to install — v9.9.9";
    private const string RestartingNote = "Weir is restarting to finish the update.";

    /// <summary>Signs in to <paramref name="own"/> and opens System › About with the live stream open.</summary>
    private async Task<IPage> OpenAboutAsync(WeirServer own)
    {
        var baseUrl = own.BaseUrl.GetLeftPart(UriPartial.Authority);
        var page = await NewPageAsync();
        await page.RunAndWaitForResponseAsync(
            () => Navigation.EnsureSignedInAsync(page, baseUrl),
            response => response.Url.EndsWith("/api/v1/activity/stream", StringComparison.Ordinal));
        await Navigation.OpenTabAsync(page, "System", "About");
        await page.EvaluateAsync("() => { window.__weirNotReloaded = true; }");
        return page;
    }

    private static async Task AssertNotReloadedAsync(IPage page) =>
        Assert.True(await page.EvaluateAsync<bool>("() => window.__weirNotReloaded === true"), "The page was reloaded.");

    private static ILocator Notice(IPage page) => page.GetByText("Update ready to install", new() { Exact = false });

    private static ILocator Restarting(IPage page) => page.GetByText(RestartingNote, new() { Exact = false });

    private static ILocator RestartButton(IPage page) => page.GetByRole(AriaRole.Button, new() { Name = "Restart to apply" });

    [E2EFact]
    public async Task An_update_the_tray_downloaded_shows_its_notice_and_clears_it_without_a_reload()
    {
        await using var own = await E2EServer.StartWindowsInstallAsync();
        var page = await OpenAboutAsync(own);
        await Expect(Notice(page)).ToHaveCountAsync(0);

        TrayFiles.WriteUpdateState(own.Home, downloaded: true, version: "9.9.9");

        await Expect(page.GetByText(ReadyNotice, new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = NoticeMs });
        await Expect(RestartButton(page)).ToBeEnabledAsync();

        TrayFiles.ClearUpdateState(own.Home);

        await Expect(Notice(page)).ToHaveCountAsync(0, new() { Timeout = NoticeMs });
        await AssertNotReloadedAsync(page);
    }

    [E2EFact]
    public async Task After_restart_to_apply_the_page_recovers_when_the_server_is_back_without_a_reload()
    {
        await using var own = await E2EServer.StartWindowsInstallAsync();
        var page = await OpenAboutAsync(own);
        TrayFiles.WriteUpdateState(own.Home, downloaded: true, version: "9.9.9");
        await Expect(page.GetByText(ReadyNotice, new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = NoticeMs });
        await RestartButton(page).ClickAsync();
        await Expect(Restarting(page)).ToBeVisibleAsync();

        // What the tray does with the flag the click wrote: installs the update, which clears its file, and starts Weir again.
        TrayFiles.ClearUpdateState(own.Home);
        await own.RestartAsync(samePort: true);

        await Expect(page.GetByTestId("live-connection-banner")).ToHaveCountAsync(0, new() { Timeout = RecoverMs });
        await Expect(Notice(page)).ToHaveCountAsync(0, new() { Timeout = RecoverMs });
        await Expect(Restarting(page)).ToHaveCountAsync(0);
        await AssertNotReloadedAsync(page);
    }

    [E2EFact]
    public async Task After_a_restart_that_did_not_install_the_update_the_notice_offers_the_restart_again()
    {
        await using var own = await E2EServer.StartWindowsInstallAsync();
        var page = await OpenAboutAsync(own);
        TrayFiles.WriteUpdateState(own.Home, downloaded: true, version: "9.9.9");
        await Expect(page.GetByText(ReadyNotice, new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = NoticeMs });
        await RestartButton(page).ClickAsync();
        await Expect(Restarting(page)).ToBeVisibleAsync();

        await own.RestartAsync(samePort: true);

        await Expect(Restarting(page)).ToHaveCountAsync(0, new() { Timeout = RecoverMs });
        await Expect(RestartButton(page)).ToBeEnabledAsync();
        await AssertNotReloadedAsync(page);
    }
}
