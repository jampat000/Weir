using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Weir.E2E.Tests.Harness;
using static Microsoft.Playwright.Assertions;

namespace Weir.E2E.Tests.Support;

/// <summary>The structural checks and screenshots the visual smoke tests share.</summary>
public static partial class PageChecks
{
    public static string ScreenshotPath(string name) => Path.Combine(RepoPaths.Screenshots, $"{name}.png");

    /// <summary>Saves a viewport-only screenshot to artifacts/screenshots/&lt;name&gt;.png.</summary>
    public static async Task SaveScreenshotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(RepoPaths.Screenshots);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = ScreenshotPath(name), FullPage = false });
    }

    /// <summary>Saves a full-page screenshot to artifacts/screenshots/&lt;name&gt;.png.</summary>
    public static async Task SaveFullPageScreenshotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(RepoPaths.Screenshots);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = ScreenshotPath(name), FullPage = true });
    }

    /// <summary>Resets the document immediately, even when smooth scrolling is enabled.</summary>
    public static async Task ScrollToTopAsync(IPage page)
    {
        await page.EvaluateAsync(
            """
            () => {
                const root = document.documentElement;
                const previous = root.style.scrollBehavior;
                root.style.scrollBehavior = 'auto';
                window.scrollTo(0, 0);
                root.style.scrollBehavior = previous;
            }
            """);
        await page.WaitForFunctionAsync("window.scrollY === 0");
    }

    /// <summary>Asserts no error-boundary overlay or generic crash message is visible.</summary>
    public static async Task AssertNoErrorStateAsync(IPage page)
    {
        await Expect(page.GetByTestId("error-boundary")).Not.ToBeVisibleAsync(new() { Timeout = 2_000 });
        await Expect(page.GetByText("Something went wrong", new() { Exact = false })).Not.ToBeVisibleAsync(new() { Timeout = 2_000 });
    }

    /// <summary>Signed-in pages must not trap wheel input inside the main shell.</summary>
    public static async Task AssertDocumentOwnsVerticalScrollAsync(IPage page)
    {
        var scroll = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const main = document.querySelector('.mm-main');
                const layout = document.querySelector('.mm-app-layout');
                return {
                    mainOverflowY: getComputedStyle(main).overflowY,
                    layoutOverflowY: getComputedStyle(layout).overflowY,
                    scrollingElement: document.scrollingElement?.tagName,
                };
            }
            """);
        var seen = scroll.ToString();
        Assert.True(scroll.GetProperty("mainOverflowY").GetString() is not ("auto" or "scroll"), seen);
        Assert.True(scroll.GetProperty("layoutOverflowY").GetString() is not ("auto" or "scroll"), seen);
        Assert.True(scroll.GetProperty("scrollingElement").GetString() == "HTML", seen);
    }

    /// <summary>
    /// A workspace page, and when it has tabs, the shared themed tab bar and accessible panel contract. A page
    /// without tabs has no tab list.
    /// </summary>
    public static async Task AssertWorkspaceAsync(IPage page, string pageTestId, string? tabsTestId)
    {
        var workspace = page.GetByTestId(pageTestId);
        await Expect(workspace).ToHaveClassAsync(WorkspacePageClass());
        if (tabsTestId is null)
        {
            await Expect(page.GetByRole(AriaRole.Tablist)).ToHaveCountAsync(0);
            return;
        }

        var tabs = page.GetByTestId(tabsTestId);
        await Expect(tabs).ToHaveClassAsync(PageTabsClass());
        var activeTab = tabs.Locator("[role='tab'][aria-selected='true']");
        await Expect(activeTab).ToHaveCountAsync(1);
        var panelId = await activeTab.GetAttributeAsync("aria-controls");
        Assert.False(string.IsNullOrEmpty(panelId), "active workspace tab must identify its panel");
        var panel = page.Locator($"#{panelId}");
        await Expect(panel).ToBeVisibleAsync();
        await Expect(panel).ToHaveAttributeAsync("aria-labelledby", (await activeTab.GetAttributeAsync("id"))!);
    }

    [GeneratedRegex(@"\bmm-workspace-page\b")]
    private static partial Regex WorkspacePageClass();

    [GeneratedRegex(@"\bmm-page-tabs\b")]
    private static partial Regex PageTabsClass();
}
