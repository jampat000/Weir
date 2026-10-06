using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Weir.E2E.Tests.Harness;
using Weir.E2E.Tests.Support;
using static Microsoft.Playwright.Assertions;

namespace Weir.E2E.Tests;

/// <summary>
/// Visual regression smoke tests for high-risk pages. Screenshots are saved to artifacts/screenshots/ for visual
/// inspection and are overwritten on each run. The artifacts folder is ignored by git, so no pixel-exact baselines
/// are committed; these are informational smoke checks. What is asserted is the structure of each screen: the
/// Dashboard at "/", each setup area and System with a row of tabs, and System › Logs with its history statement.
/// </summary>
public sealed class VisualSmokeTests(E2EServer server) : E2ETestBase(server)
{
    private static readonly ViewportSize Desktop = new() { Width = 1600, Height = 900 };
    private static readonly ViewportSize Phone = new() { Width = 390, Height = 844 };

    // Every setup area, as named in the side menu.
    private static readonly string[] SetupAreaLabels = ["Workflows", "Connections", "Rules", "Performance"];

    private static readonly PageGotoOptions DomContentLoaded = new() { WaitUntil = WaitUntilState.DOMContentLoaded };

    /// <summary>
    /// A retired address such as /dashboard gets the not-found page, which offers the way to the Dashboard. The
    /// Dashboard lives at "/", as Processing always did, so no address was ever saved as /dashboard.
    /// </summary>
    [E2EFact]
    public async Task Old_dashboard_address_is_not_found()
    {
        var page = await NewPageAsync(Desktop);

        await Navigation.EnsureSignedInAsync(page, BaseUrl);

        await page.GotoAsync($"{BaseUrl}/dashboard", DomContentLoaded);
        await Expect(page).ToHaveURLAsync(new Regex(".*/dashboard"));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "This page doesn't exist.", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Dashboard", Exact = true }).First).Not.ToHaveAttributeAsync("aria-current", "page");
        await PageChecks.AssertNoErrorStateAsync(page);

        await page.GetByRole(AriaRole.Link, new() { Name = "Go to Dashboard", Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex(@".*/(?:$|[?#])"));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard", Exact = true })).ToBeVisibleAsync();
    }

    /// <summary>Weir lands on the Dashboard at "/": every file Weir is working on. There is no Home page.</summary>
    [E2EFact]
    public async Task Dashboard_is_the_landing_page()
    {
        var page = await NewPageAsync(Desktop);

        await Navigation.EnsureSignedInAsync(page, BaseUrl);
        await page.GotoAsync($"{BaseUrl}/", DomContentLoaded);

        await Expect(page.GetByTestId("processing-page")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Home", Exact = true })).ToHaveCountAsync(0);
        // Page content only: the shell brand line is replaced by the Weir rename (#458).
        await Expect(page.Locator("main").GetByText("your library", new() { Exact = false })).ToHaveCountAsync(0);
        await PageChecks.AssertDocumentOwnsVerticalScrollAsync(page);
        await PageChecks.AssertNoErrorStateAsync(page);
        await PageChecks.SaveScreenshotAsync(page, "processing");
    }

    /// <summary>System › Logs states its history horizon plainly (#469).</summary>
    [E2EFact]
    public async Task History_says_how_far_back_it_goes()
    {
        var page = await NewPageAsync(Desktop);

        await Navigation.EnsureSignedInAsync(page, BaseUrl);
        await Navigation.OpenLogsAsync(page);

        await Expect(page).ToHaveURLAsync(new Regex(@".*/system\?tab=logs(?:$|[&#])"));
        await Expect(page.GetByTestId("log-feed")).ToBeVisibleAsync();
        await PageChecks.AssertNoErrorStateAsync(page);
        await PageChecks.SaveScreenshotAsync(page, "logs");

        await page.GetByRole(AriaRole.Button, new() { Name = "Log settings", Exact = true }).ClickAsync();
        await Expect(page.GetByTestId("suite-settings-retention")).ToContainTextAsync("How long things are kept");
        await PageChecks.AssertNoErrorStateAsync(page);
        await PageChecks.SaveScreenshotAsync(page, "logs-settings");
    }

    /// <summary>Every setup area and System tab opens, and the document keeps the scroll.</summary>
    [E2EFact]
    public async Task Setup_and_system_tabs_render()
    {
        var page = await NewPageAsync(Desktop);

        await Navigation.EnsureSignedInAsync(page, BaseUrl);

        // Each setup area is an entry of its own in the side menu, with its tabs across the top, as System has.
        foreach (var label in SetupAreaLabels)
        {
            await Navigation.OpenSidebarAsync(page, label);
            await Expect(page.GetByTestId("suite-settings-page")).ToBeVisibleAsync();
            // The previous area's tabs stay until this one has drawn, so count them only once its title shows.
            await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1, Name = label, Exact = true })).ToBeVisibleAsync();
            await ClickEveryTabAsync(page, page.GetByTestId("setup-area-tabs").GetByRole(AriaRole.Tab));
        }

        await Navigation.OpenSidebarAsync(page, "System");
        await Expect(page.GetByTestId("suite-system-page")).ToBeVisibleAsync();
        await ClickEveryTabAsync(page, page.GetByTestId("system-section-tabs").GetByRole(AriaRole.Tab));

        await Navigation.OpenTabAsync(page, "System", "Security");
        await Expect(page.GetByTestId("suite-settings-security")).ToBeVisibleAsync();
        await PageChecks.ScrollToTopAsync(page);
        await PageChecks.SaveFullPageScreenshotAsync(page, "system-security-full");

        await Navigation.OpenTabAsync(page, "System", "About");
        await Expect(page.GetByTestId("suite-settings-global")).ToBeVisibleAsync();

        await PageChecks.AssertDocumentOwnsVerticalScrollAsync(page);
        await PageChecks.AssertNoErrorStateAsync(page);
        await PageChecks.ScrollToTopAsync(page);
        await PageChecks.SaveScreenshotAsync(page, "system-instance");
        await PageChecks.SaveFullPageScreenshotAsync(page, "system-instance-full");
    }

    /// <summary>The setup areas and System use the shared horizontal tab bar without page overflow.</summary>
    [E2EFact]
    public async Task Setup_and_system_share_themed_tabs_and_responsive_layout()
    {
        var context = await NewContextAsync(Desktop);
        var page = await context.NewPageAsync();
        ApplyDefaultTimeout(page);
        await Navigation.EnsureSignedInAsync(page, BaseUrl);

        foreach (var (label, pageTestId, tabsTestId, screenshotName) in new[]
        {
            ("Workflows", "suite-settings-page", "setup-area-tabs", "settings-workspace"),
            ("System", "suite-system-page", "system-section-tabs", "system-workspace"),
        })
        {
            await Navigation.OpenSidebarAsync(page, label);
            await PageChecks.AssertWorkspaceAsync(page, pageTestId, tabsTestId);
            await PageChecks.AssertDocumentOwnsVerticalScrollAsync(page);
            await PageChecks.AssertNoErrorStateAsync(page);
            await PageChecks.ScrollToTopAsync(page);
            await PageChecks.SaveScreenshotAsync(page, screenshotName);

            var mobilePage = await context.NewPageAsync();
            try
            {
                await mobilePage.SetViewportSizeAsync(Phone.Width, Phone.Height);
                ApplyDefaultTimeout(mobilePage);
                await mobilePage.GotoAsync(page.Url, DomContentLoaded);
                await PageChecks.AssertWorkspaceAsync(mobilePage, pageTestId, tabsTestId);
                Assert.True(
                    await mobilePage.EvaluateAsync<bool>("document.documentElement.scrollWidth <= document.documentElement.clientWidth"),
                    $"{label} overflows the narrow viewport");
                await PageChecks.AssertDocumentOwnsVerticalScrollAsync(mobilePage);
                await PageChecks.AssertNoErrorStateAsync(mobilePage);
                await PageChecks.SaveScreenshotAsync(mobilePage, $"{screenshotName}-mobile");
            }
            finally
            {
                await mobilePage.CloseAsync();
            }
        }
    }

    /// <summary>Setup › Rules › Profiles: the full audio and subtitle profile editor stays readable at desktop width.</summary>
    [E2EFact]
    public async Task Rules_editor_renders()
    {
        var page = await NewPageAsync(Desktop);
        await Navigation.EnsureSignedInAsync(page, BaseUrl);

        await Navigation.OpenTabAsync(page, "Rules", "Profiles");
        await Expect(page.GetByTestId("processing-rule-set-workspace")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "New profile", Exact = true }).ClickAsync();
        // The profile bar: the picker, the name and who uses it on one line; the field is "Name".
        await Expect(page.GetByTestId("rule-set-profile-bar").GetByLabel("Name", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByText("Audio order", new() { Exact = true })).Not.ToBeVisibleAsync();

        await PageChecks.AssertDocumentOwnsVerticalScrollAsync(page);
        await PageChecks.AssertNoErrorStateAsync(page);
        await PageChecks.ScrollToTopAsync(page);
        await PageChecks.SaveScreenshotAsync(page, "settings-rules");
        await PageChecks.SaveFullPageScreenshotAsync(page, "settings-rules-full");
        await page.SetViewportSizeAsync(Phone.Width, Phone.Height);
        // At phone width the side menu slides out; it turns hidden only once the slide has finished.
        await Expect(page.GetByTestId("shell-nav-toggle")).ToBeVisibleAsync();
        await Expect(page.Locator("#mm-primary-sidebar")).ToBeHiddenAsync();
        await PageChecks.ScrollToTopAsync(page);
        await PageChecks.SaveScreenshotAsync(page, "settings-rules-mobile");
    }

    private static async Task ClickEveryTabAsync(IPage page, ILocator tabs)
    {
        var count = await tabs.CountAsync();
        for (var index = 0; index < count; index++)
        {
            var tab = tabs.Nth(index);
            await tab.ClickAsync();
            await Expect(tab).ToHaveAttributeAsync("aria-selected", "true");
            await PageChecks.AssertNoErrorStateAsync(page);
        }
    }
}
