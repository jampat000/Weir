using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Weir.LiveAudit;

// Sidebar navigation, the desktop and mobile shell chrome, and the Dashboard, Library and Logs screens.
internal sealed partial class LiveAudit
{
    private static readonly string[] PrimaryNavigation =
    [
        "Dashboard",
        "Activity",
        "Library",
        "Workflows",
        "Connections",
        "Rules",
        "Performance",
        "System",
    ];

    private async Task OpenSidebarAsync(string label)
    {
        // A link that carries a count is named for it ("Processing, 2 working", "Activity, 1 need you").
        var link = page.GetByRole(
            AriaRole.Link,
            new PageGetByRoleOptions { NameRegex = new Regex($"^{Regex.Escape(label)}(?:,|$)") });
        await ClickAsync(link, $"open {label} from primary navigation");
    }

    private async Task AssertNoVisibleCrashAsync()
    {
        var boundary = page.GetByTestId("error-boundary");
        if (await boundary.CountAsync() > 0)
        {
            Require(!await boundary.IsVisibleAsync(), "error boundary is visible");
        }

        Require(
            await page.GetByText("Something went wrong").CountAsync() == 0,
            "generic error state is visible");
    }

    private async Task ShellAndResponsiveAsync()
    {
        await page.SetViewportSizeAsync(1_440, 1_000);
        await page.GotoAsync(config.BaseUrl + "/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await VisibleAsync(page.GetByTestId("shell-ready"), "desktop shell");
        var collapse = page.GetByTestId("sidebar-collapse");
        Require(await collapse.GetAttributeAsync("aria-expanded") == "true", "sidebar starts expanded");
        // The mark, not its block: the name beside it folds away on collapse by design.
        var logo = page.Locator(".mm-sidebar .mm-logo-mark");
        var expandedLogo = await logo.BoundingBoxAsync();
        await ClickAsync(collapse, "collapse sidebar");
        Require(await collapse.GetAttributeAsync("aria-expanded") == "false", "sidebar collapses");
        await page.WaitForTimeoutAsync(600);
        var collapsedLogo = await logo.BoundingBoxAsync();
        Require(
            expandedLogo is not null
            && collapsedLogo is not null
            && Math.Abs(expandedLogo.X - collapsedLogo.X) <= 1
            && Math.Abs(expandedLogo.Y - collapsedLogo.Y) <= 1
            && Math.Abs(expandedLogo.Width - collapsedLogo.Width) <= 1
            && Math.Abs(expandedLogo.Height - collapsedLogo.Height) <= 1,
            $"the logo moved or resized when the sidebar collapsed: {Describe(expandedLogo)} -> {Describe(collapsedLogo)}");
        await ClickAsync(collapse, "expand sidebar");
        Require(await collapse.GetAttributeAsync("aria-expanded") == "true", "sidebar expands");

        var theme = page.GetByTestId("theme-toggle");
        var before = await page.Locator("html").GetAttributeAsync("data-mm-theme");
        await ClickAsync(theme, "toggle application theme");
        var after = await page.Locator("html").GetAttributeAsync("data-mm-theme");
        Require(after is "dark" or "light" && after != before, "theme toggle did not apply");
        await ClickAsync(theme, "toggle application theme back");

        await page.SetViewportSizeAsync(390, 844);
        await page.GotoAsync(config.BaseUrl + "/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var menu = page.GetByTestId("shell-nav-toggle");
        await ClickAsync(menu, "open mobile navigation");
        var closeNavigation = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Close navigation" });
        var backdrop = await VisibleAsync(closeNavigation, "mobile navigation backdrop");
        // The drawer covers the left of the backdrop, its middle included, so tap the dimmed strip beside it, where
        // a person would.
        var box = await backdrop.BoundingBoxAsync();
        Require(box is not null, "mobile navigation backdrop has no box");
        await backdrop.ClickAsync(new LocatorClickOptions
        {
            Position = new Position { X = box!.Width - 12, Y = box.Height / 2 },
        });
        await page.WaitForTimeoutAsync(120);
        Require(await closeNavigation.CountAsync() == 0, "mobile navigation did not close");
        await page.SetViewportSizeAsync(1_440, 1_000);
        Record("desktop collapse/theme and mobile navigation controls");
    }

    /// <summary>
    /// A tab of a setup area (Workflows, Connections, Rules, Performance) or of System. Each area is an entry of its
    /// own in the side menu, which marks the one showing, and has its tabs across the top of the page.
    /// </summary>
    private async Task OpenTabAsync(string sidebar, string tab)
    {
        await OpenSidebarAsync(sidebar);
        await VisibleAsync(
            page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = sidebar, Exact = true })
                .And(page.Locator("[aria-current='page']")),
            $"{sidebar} marked as the current page in the side menu");
        await ClickAsync(
            page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = tab, Exact = true }),
            $"open {sidebar} › {tab}");
        await VisibleAsync(
            page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = tab, Exact = true, Selected = true }),
            $"{sidebar} › {tab} selected");
    }

    /// <summary>System › Logs, the one log of Weir's events, jobs and server log; <paramref name="source"/> names a Source chip to press.</summary>
    private async Task OpenLogsAsync(string? source = null)
    {
        await OpenTabAsync("System", "Logs");
        await VisibleAsync(page.GetByTestId("log-summary"), "Logs summary");
        if (source is not null)
        {
            var chip = page.GetByRole(AriaRole.Group, new PageGetByRoleOptions { Name = "Source" })
                .GetByRole(
                    AriaRole.Button,
                    new LocatorGetByRoleOptions { NameRegex = new Regex($"^{Regex.Escape(source)}") });
            await ClickAsync(chip, $"press the {source} source chip");
            Require(
                await chip.GetAttributeAsync("aria-pressed") == "true",
                $"the {source} source chip is not pressed");
        }
    }

    private async Task<List<string>> TabLabelsAsync(string tabsTestId)
    {
        var tabs = page.GetByTestId(tabsTestId).GetByRole(AriaRole.Tab);
        return [.. (await tabs.AllTextContentsAsync()).Select(label => label.Trim())];
    }

    private async Task ProcessingLiveAsync()
    {
        await OpenSidebarAsync("Dashboard");
        await VisibleAsync(page.GetByTestId("processing-page"), "Dashboard page");
        await VisibleAsync(
            page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Dashboard", Exact = true }),
            "Dashboard heading");
        // The landing page is the Dashboard, every file's story is Activity, and the events are System › Logs.
        // Each setup area is an entry of its own.
        var primary = page.GetByRole(AriaRole.Navigation, new PageGetByRoleOptions { Name = "Primary" });
        var labels = (await primary.Locator(".mm-sidebar-link-label").AllTextContentsAsync())
            .Select(text => text.Trim())
            .ToList();
        Require(labels.SequenceEqual(PrimaryNavigation), $"primary navigation is [{string.Join(", ", labels)}]");
        foreach (var retired in new[] { "Home", "Processing", "History" })
        {
            Require(
                await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = retired, Exact = true }).CountAsync() == 0,
                $"{retired} must not appear in the sidebar");
        }

        await AssertNoVisibleCrashAsync();
        await ScreenshotAsync("processing");
        Record("Dashboard main screen and the side menu");
    }

    private async Task LibraryAsync()
    {
        await OpenSidebarAsync("Library");
        await VisibleAsync(page.GetByTestId("library-page"), "Library page");
        await AssertNoVisibleCrashAsync();
        await ScreenshotAsync("library");
        Record("Library screen");
    }

    private async Task HistoryActivityAsync()
    {
        await OpenLogsAsync();
        await VisibleAsync(page.GetByTestId("log-feed"), "Log");
        var filters = await VisibleAsync(page.GetByTestId("logs-controls"), "Log filters");
        Require(
            await page.GetByText("All modules", new PageGetByTextOptions { Exact = true }).CountAsync() == 0,
            "Logs still shows a Module filter");
        Require(
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Apply filters", Exact = true }).CountAsync() == 0,
            "Logs still has an Apply button");
        var level = page.GetByTestId("logs-level-picker");
        await ClickAsync(level, "open the level picker");
        // The picker offers only the levels the log has entries for, so this takes the first of those.
        var firstLevel = await VisibleAsync(
            page.GetByRole(AriaRole.Option).First,
            "a level the log has entries for");
        var chosenLevel = (await firstLevel.InnerTextAsync()).Split(" · ")[0].Trim();
        await ClickAsync(firstLevel, "narrow the log to a level");
        await ClickAsync(level, "close the level picker");
        Require((await level.InnerTextAsync()).Trim() == chosenLevel, $"the level picker does not say {chosenLevel}");
        await filters.GetByRole(AriaRole.Searchbox, new LocatorGetByRoleOptions { Name = "Search the log" }).FillAsync("audit");
        // The pickers sit on the title line or in the Log card, wherever they fit, so they are found on the page.
        await ClickAsync(page.GetByTestId("logs-when-picker"), "open the time picker");
        await ClickAsync(
            page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = "Custom range", Exact = true }),
            "choose a custom range");
        var rangeFields = await VisibleAsync(page.GetByTestId("logs-range"), "custom range");
        await rangeFields.Locator("input[type=\"datetime-local\"]").Nth(0).FillAsync("2026-01-01T00:00");
        await rangeFields.Locator("input[type=\"datetime-local\"]").Nth(1).FillAsync("2026-12-31T23:59");
        await ClickAsync(
            page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Clear filters", Exact = true }),
            "clear the log's filters");
        await page.WaitForTimeoutAsync(500);
        Require((await level.InnerTextAsync()).Trim() == "All levels", "the log's filters did not clear");
        await VisibleAsync(page.GetByTestId("logs-export"), "Log export menu");
        await ScreenshotAsync("history-activity");
        Record("Logs: one log of events, jobs and the server, its filters and its actions");
    }

    private static string Describe(LocatorBoundingBoxResult? box) =>
        box is null ? "null" : $"{{x: {box.X}, y: {box.Y}, width: {box.Width}, height: {box.Height}}}";
}
